module FsharpToolbox.Pkg.Cap.Outbox.ServiceCollectionExtensions

open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Options

open DotNetCore.CAP
open DotNetCore.CAP.Persistence
open DotNetCore.CAP.PostgreSql

open FsharpToolbox.Pkg.Communication.Core

open FsharpToolbox.Pkg.Serialization.Json


type private NullStorageInitializer(logger: ILogger<PostgreSqlStorageInitializer>, options: IOptions<PostgreSqlOptions>) =
  let pgStorageInitializer =
    PostgreSqlStorageInitializer(logger, options)

  interface IStorageInitializer with
    member this.GetPublishedTableName() =
      pgStorageInitializer.GetPublishedTableName()

    member this.GetReceivedTableName() =
      pgStorageInitializer.GetReceivedTableName()

    member this.InitializeAsync(_cancellationToken) =
      logger.LogDebug(
        "Skipping storage initialization, make sure tables {t1:l} and {t2:l} have been created",
        (this :> IStorageInitializer)
          .GetPublishedTableName(),
        (this :> IStorageInitializer)
          .GetReceivedTableName()
      )

      Task.CompletedTask


type IServiceCollection with
  member services.AddCapServices<'dbcontext when 'dbcontext :> DbContext>(settings: TopicSettings) =
    services.AddCap
      (fun capOptions ->
        capOptions.JsonSerializerOptions |> Serializer.updateSerializeOptions |> ignore
        capOptions
          .UseEntityFramework<'dbcontext>()
          .UseAzureServiceBus(fun options ->
            options.EnableSessions <- true
            options.TopicPath <- settings.TopicName
            options.ConnectionString <- settings.ConnectionString)
        |> ignore)
    |> ignore

    services.AddSingleton<IStorageInitializer, NullStorageInitializer>()
    |> ignore

    services
