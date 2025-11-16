namespace StackProxy.Adapter

open System

module Routing =
  let private sanitize (value: string) =
    let sanitized =
      value
      |> Seq.map (fun ch -> if Char.IsLetterOrDigit ch then Char.ToLowerInvariant ch else '_')
      |> Array.ofSeq
      |> String
    sanitized.Trim('_')

  type RouteDefinition =
    { BackendName: string
      ServerAddress: string
      HostMatch: string
      Mode: Protocol }

  let describe (service: ServiceMetadata) : RouteDefinition =
    let backend =
      match service.Mode with
      | Protocol.Http -> $"http_{sanitize service.Host}"
      | Protocol.Tcp -> $"tcp_{sanitize service.Host}"

    { BackendName = backend
      ServerAddress = $"{service.ContainerAddress}:{service.LocalPort}"
      HostMatch = service.Host
      Mode = service.Mode }
