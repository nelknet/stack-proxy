namespace StackProxy.Adapter

module ServiceRegistry =
  type State = private { Services: Map<string, ServiceMetadata> }

  let empty = { Services = Map.empty }

  let upsert containerId metadata state =
    { state with Services = state.Services |> Map.add containerId metadata }

  let remove containerId state =
    { state with Services = state.Services |> Map.remove containerId }

  let asList state =
    state.Services |> Map.toList |> List.map snd
