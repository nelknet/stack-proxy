namespace StackProxy.Adapter

open System
open System.Collections.Generic

[<CLIMutable>]
type ContainerInfo =
  { Names: string list
    Labels: IReadOnlyDictionary<string, string>
    ExposedPorts: int list }

module ContainerInfo =
  let private sanitizeName (value: string) =
    if String.IsNullOrWhiteSpace(value) then
      ""
    else
      value.Trim().TrimStart('/')

  let private tryLabel key (labels: IReadOnlyDictionary<string, string>) =
    match labels.TryGetValue key with
    | true, value when not (String.IsNullOrWhiteSpace value) -> Some value
    | _ -> None

  let private inferServiceName (info: ContainerInfo) =
    match tryLabel "com.docker.compose.service" info.Labels with
    | Some value -> value
    | None ->
        match info.Names with
        | head :: _ when not (String.IsNullOrWhiteSpace head) -> sanitizeName head
        | _ -> "unknown"

  let private inferProjectName (info: ContainerInfo) =
    tryLabel "com.docker.compose.project" info.Labels

  let toRaw (info: ContainerInfo) : RawServiceInput =
    { ServiceName = inferServiceName info
      ProjectName = inferProjectName info
      Labels = info.Labels
      ExposedPorts = info.ExposedPorts }
