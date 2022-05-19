namespace FsharpToolbox.Pkg.Cap.Outbox

open System.Data
open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Storage

type Transaction =
  | Inherited of IDbContextTransaction
  | Local of IDbContextTransaction

// PostgreSQL doesn't support nested or concurrent transactions, which means that a method that
// creates a transaction can normally not be called when a transaction is already in progress.
//
// The class type below tries to work around this limitation by only creating a new transaction
// unless there is an ongoing transaction. When a new transaction has been created, all calls
// to the transaction API is delegated to the wrapped instance. Otherwise (when transaction is
// inherited), calls to the transaction API will be no-ops and commit and rollback have to be
// handled by the caller, who created the ongoing transaction.

type MaybeTransaction (transaction: Transaction) =

  static member InheritOrCreate (ctx: DbContext, isolationLevel: IsolationLevel) =
    let transaction =
      match ctx.Database.CurrentTransaction with
      | null ->                  Local (ctx.Database.BeginTransaction(isolationLevel))
      | inheritedTransaction ->  Inherited inheritedTransaction

    new MaybeTransaction(transaction) :> IDbContextTransaction

  /// Returns the wrapped transaction, regardless of whether it is a local or
  /// inherited transaction.
  member private this.wrappedTransaction =
    match transaction with
    | Local t     -> t
    | Inherited t -> t

  interface IDbContextTransaction with

    member this.Commit() =
      match transaction with
      | Local t     -> t.Commit()
      | Inherited _ -> ()

    member this.CommitAsync(cancellationToken) =
      match transaction with
      | Local t     -> t.CommitAsync(cancellationToken)
      | Inherited _ -> Task.CompletedTask

    member this.CreateSavepoint(name) =
      this.wrappedTransaction.CreateSavepoint(name)

    member this.CreateSavepointAsync(name, cancellationToken) =
      this.wrappedTransaction.CreateSavepointAsync(name, cancellationToken)

    member this.Dispose() =
      match transaction with
      | Local t     -> t.Dispose()
      | Inherited t -> ()

    member this.DisposeAsync() =
      match transaction with
      | Local t     -> t.DisposeAsync()
      | Inherited _ -> ValueTask.CompletedTask

    member this.ReleaseSavepoint(name) =
      this.wrappedTransaction.ReleaseSavepoint(name)

    member this.ReleaseSavepointAsync(name, cancellationToken) =
      this.wrappedTransaction.ReleaseSavepointAsync(name, cancellationToken)

    member this.Rollback() =
      match transaction with
      | Local t     -> t.Rollback()
      | Inherited _ -> ()

    member this.RollbackAsync(cancellationToken) =
      match transaction with
      | Local t     -> t.RollbackAsync(cancellationToken)
      | Inherited _ -> Task.CompletedTask

    member this.RollbackToSavepoint(name) =
      this.wrappedTransaction.RollbackToSavepoint(name)

    member this.RollbackToSavepointAsync(name, cancellationToken) =
      this.wrappedTransaction.RollbackToSavepointAsync(name, cancellationToken)

    member this.SupportsSavepoints =
      this.wrappedTransaction.SupportsSavepoints

    member this.TransactionId =
      this.wrappedTransaction.TransactionId
