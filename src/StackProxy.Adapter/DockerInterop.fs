namespace StackProxy.Adapter

open System
open System.Collections.Generic
open Docker.DotNet.Models

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

  let fromListResponse (response: ContainerListResponse) =
    let names =
      if isNull response.Names then
        []
      else
        response.Names |> Seq.map sanitizeName |> List.ofSeq

    let labels : IReadOnlyDictionary<string, string> =
      if isNull response.Labels then
        upcast Dictionary<string, string>()
      else
        upcast Dictionary<string, string>(response.Labels)

    let ports =
      if isNull response.Ports then
        []
      else
        response.Ports
        |> Seq.choose (fun p ->
          let portValue = int p.PrivatePort
          if portValue <= 0 then None else Some portValue)
        |> List.ofSeq

    { Names = names
      Labels = labels
      ExposedPorts = ports }

  let fromInspectResponse (response: ContainerInspectResponse) =
    let names =
      if String.IsNullOrWhiteSpace response.Name then
        []
      else
        [ sanitizeName response.Name ]

    let labels : IReadOnlyDictionary<string, string> =
      if isNull response.Config || isNull response.Config.Labels then
        upcast Dictionary<string, string>()
      else
        upcast Dictionary<string, string>(response.Config.Labels)

    let ports =
      if isNull response.NetworkSettings || isNull response.NetworkSettings.Ports then
        []
      else
        response.NetworkSettings.Ports
        |> Seq.choose (fun kv ->
          let key = kv.Key
          if String.IsNullOrWhiteSpace key then None
          else
            let parts = key.Split('/', StringSplitOptions.RemoveEmptyEntries)
            match parts with
            | [| portStr |]
            | [| portStr; _ |] ->
              match Int32.TryParse(portStr) with
              | true, value when value > 0 -> Some value
              | _ -> None
            | _ -> None)
        |> Seq.distinct
        |> List.ofSeq

    { Names = names
      Labels = labels
      ExposedPorts = ports }
