namespace StackProxy.Adapter

module Reconciler =
  type ServiceEvent =
    | Upsert of containerId: string * metadata: ServiceMetadata
    | Remove of containerId: string

  let applyEvents writeConfig state events =
    let nextState =
      events
      |> List.fold
        (fun acc evt ->
          match evt with
          | Upsert (containerId, metadata) -> ServiceRegistry.upsert containerId metadata acc
          | Remove containerId -> ServiceRegistry.remove containerId acc)
        state

    let services = ServiceRegistry.asList nextState
    writeConfig services
    nextState
