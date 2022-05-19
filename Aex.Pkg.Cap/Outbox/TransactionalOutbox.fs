namespace FsharpToolbox.Pkg.Cap.Outbox

open DotNetCore.CAP
open DotNetCore.CAP.AzureServiceBus
open Microsoft.EntityFrameworkCore
open System.Collections.Generic
open FSharpPlus

open FsharpToolbox.Pkg.FpUtils
open FsharpToolbox.Pkg.Logging


module TransactionalOutbox =
  type BusinessOperation<'t,'terror> = unit -> Async<Result<'t, 'terror>>
  type GenerateEvents<'a, 'terror> = 'a -> Async<Result<Event list, 'terror>>
  type ErrorCreator<'terror> = exn -> 'terror

  type ITransactionalOutbox =
    abstract member withTransaction: BusinessOperation<'t, 'terror> -> GenerateEvents<'t, 'terror> -> ErrorCreator<'terror> -> Async<Result<'t, 'terror>>

  let private buildHeaders aexEvent =
    Dictionary<string, string>()
    |> tee
         (fun headers ->
           headers.Add("eventName", aexEvent.name)
           headers.Add("version", aexEvent.version)
           Option.iter (fun sessionId -> headers.Add(AzureServiceBusHeaders.SessionId, sessionId)) aexEvent.sessionId)

  let private publishEvent (capBus: ICapPublisher) aexEvent =
    let extraHeaders = buildHeaders aexEvent
    async {
      do!
        capBus.PublishAsync(aexEvent.name, aexEvent.payload, extraHeaders)
        |> Async.AwaitTask
    }

  let private publishEvents capBus aexEvents =
    aexEvents
    |> List.map (publishEvent capBus)
    |> Async.Sequential
    |> Async.map (fun _ -> Ok ())

  let private handleExceptions (errorCreator: ErrorCreator<'terror>) (result: Async<Result<_,'terror>>) =
    result
    |> Async.Catch
    |> Async.map (function
                   | Choice1Of2 res ->
                       res
                   | Choice2Of2 ex ->
                     L.Error(ex, "Failed to publish events for transactional outbox")
                     errorCreator ex |> Error)

  type private TransactionalOutboxImpl (dbContext: DbContext, capBus: ICapPublisher) =
    interface ITransactionalOutbox with
      member _.withTransaction
        (doOperations: BusinessOperation<'t, 'terror>)
        (generateEvents: GenerateEvents<'t, 'terror>)
        (errorCreator: ErrorCreator<'terror>)
        =
        use trans =
          dbContext.Database.BeginTransaction(capBus, autoCommit = false)

        let operationsResult = doOperations () |> Async.RunSynchronously

        operationsResult
        >>= (generateEvents >> Async.RunSynchronously)
        >>= fun events ->
              events
              |> ((publishEvents capBus) >> Async.RunSynchronously)
              |>! errorCreator
              >>= fun _ -> operationsResult
              |== fun _ -> trans.Commit()
              |=! fun _ -> trans.Rollback()
        |> Async.retn

  type TransactionalOutboxMock () =

    let mutable events = []

    member this.Events with get () = events

    interface ITransactionalOutbox with
      member this.withTransaction
          (doOperations: BusinessOperation<'t, 'terror>)
          (createEvents: GenerateEvents<'t, 'terror>)
          (errorCreator: ErrorCreator<'terror>)
          =
            doOperations ()
            %|== fun t ->
                   let eventResult = createEvents t
                   let _events = eventResult |> Async.RunSynchronously |> Result.get
                   events <- _events

  let setup (dbContext: DbContext) (capBus: ICapPublisher) =
    TransactionalOutboxImpl(dbContext, capBus) :> ITransactionalOutbox
