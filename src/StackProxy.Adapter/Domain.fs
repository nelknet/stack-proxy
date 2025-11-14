namespace StackProxy.Adapter

open System
open System.Collections.Generic

[<RequireQualifiedAccess>]
type Protocol =
  | Http
  | Tcp

[<CLIMutable>]
type ServiceMetadata =
  { ServiceName: string
    ProjectName: string option
    Host: string
    Mode: Protocol
    LocalPort: int
    PublicPort: int option }

[<CLIMutable>]
type RawServiceInput =
  { ServiceName: string
    ProjectName: string option
    Labels: IReadOnlyDictionary<string, string>
    ExposedPorts: int list }

module Metadata =
  [<Literal>]
  let LabelPrefix = "stack-proxy"

  let private label key = $"{LabelPrefix}.{key}"

  let private tryGet (key: string) (labels: IReadOnlyDictionary<string, string>) =
    match labels.TryGetValue(key) with
    | true, value when not (String.IsNullOrWhiteSpace value) -> Some value
    | _ -> None

  let private tryGetInt key labels =
    match tryGet key labels with
    | Some value ->
        match Int32.TryParse(value) with
        | true, parsed -> Some parsed
        | _ -> None
    | None -> None

  let private isDisabled labels =
    match tryGet (label "disable") labels with
    | Some value when value.Equals("true", StringComparison.OrdinalIgnoreCase) -> true
    | _ -> false

  let private parseMode labels =
    match tryGet (label "mode") labels with
    | Some value when value.Equals("tcp", StringComparison.OrdinalIgnoreCase) -> Protocol.Tcp
    | _ -> Protocol.Http

  let private defaultHost serviceName projectName =
    match projectName with
    | Some project when not (String.IsNullOrWhiteSpace project) -> $"{serviceName}.{project}.local"
    | _ -> $"{serviceName}.local"

  let private inferHost raw labels =
    match tryGet (label "host") labels with
    | Some host -> host
    | None -> defaultHost raw.ServiceName raw.ProjectName

  let private inferLocalPort mode raw labels =
    match tryGetInt (label "localport") labels with
    | Some value -> value
    | None ->
        match raw.ExposedPorts with
        | head :: _ -> head
        | [] when mode = Protocol.Http -> 80
        | [] -> 15432

  let private inferPublicPort labels =
    tryGetInt (label "publicport") labels

  let tryCreate (raw: RawServiceInput) : ServiceMetadata option =
    if isDisabled raw.Labels then
      None
    else
      let mode = parseMode raw.Labels
      let host = inferHost raw raw.Labels
      let localPort = inferLocalPort mode raw raw.Labels
      let publicPort = inferPublicPort raw.Labels

      Some
        { ServiceName = raw.ServiceName
          ProjectName = raw.ProjectName
          Host = host
          Mode = mode
          LocalPort = localPort
          PublicPort = publicPort }
