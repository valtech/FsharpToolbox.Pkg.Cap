namespace FsharpToolbox.Pkg.Cap.Outbox

type Event =
  {
    name: string
    version: string
    sessionId: string option
    payload: obj
  }
