module StackProxy.Adapter.Tests.MetadataTests

open System.Collections.Generic
open Xunit
open StackProxy.Adapter

let private labels (pairs: (string * string) list) =
  let dict = Dictionary<string, string>()
  for (k, v) in pairs do
    dict[k] <- v
  dict :> IReadOnlyDictionary<_, _>

let private raw service project labelPairs ports =
  { RawServiceInput.ServiceName = service
    ProjectName = project
    Labels = labels labelPairs
    ExposedPorts = ports }

let private getMetadata input =
  match Metadata.tryCreate input with
  | Some meta -> meta
  | None -> failwith "Expected metadata, got None"

[<Fact>]
let ``falls back to project-based host and exposed port`` () =
  let input = raw "moneydevkit" (Some "mdk-100") [] [ 8888 ]
  let meta = getMetadata input
  Assert.Equal("moneydevkit.mdk-100.local", meta.Host)
  Assert.Equal(8888, meta.LocalPort)
  Assert.Equal(Protocol.Http, meta.Mode)

[<Fact>]
let ``respects explicit host and local port labels`` () =
  let input =
    raw
      "moneydevkit"
      (Some "mdk-100")
      [ ("mdk.host", "custom.host.local"); ("mdk.localport", "3900") ]
      []
  let meta = getMetadata input
  Assert.Equal("custom.host.local", meta.Host)
  Assert.Equal(3900, meta.LocalPort)

[<Fact>]
let ``infers tcp mode and default port when none exposed`` () =
  let input =
    raw
      "postgres"
      (Some "mdk-101")
      [ ("mdk.mode", "tcp") ]
      []
  let meta = getMetadata input
  Assert.Equal(Protocol.Tcp, meta.Mode)
  Assert.Equal(15432, meta.LocalPort)

[<Fact>]
let ``returns none when disabled label present`` () =
  let input = raw "admin" None [ ("mdk.disable", "true") ] [ 3000 ]
  let result = Metadata.tryCreate input
  Assert.True(result.IsNone)

[<Fact>]
let ``routing descriptor normalizes host names`` () =
  let meta =
    { ServiceMetadata.ServiceName = "moneydevkit.com"
      ProjectName = Some "mdk-200"
      Host = "MoneyDevkit-APP.mdk-200.local"
      Mode = Protocol.Http
      LocalPort = 8888
      PublicPort = None }

  let route = Routing.describe meta
  Assert.Equal("http_moneydevkit_app_mdk_200_local", route.BackendName)
  Assert.Equal("moneydevkit.com:8888", route.ServerAddress)

[<Fact>]
let ``tcp routing descriptor uses tcp prefix`` () =
  let meta =
    { ServiceMetadata.ServiceName = "postgres"
      ProjectName = Some "mdk-201"
      Host = "postgres.mdk-201.local"
      Mode = Protocol.Tcp
      LocalPort = 5432
      PublicPort = None }

  let route = Routing.describe meta
  Assert.Equal("tcp_postgres_mdk_201_local", route.BackendName)
  Assert.Equal("postgres:5432", route.ServerAddress)

[<Fact>]
let ``renders http and tcp sections`` () =
  let services =
    [ { ServiceMetadata.ServiceName = "moneydevkit.com"
        ProjectName = Some "mdk-200"
        Host = "moneydevkit.mdk-200.local"
        Mode = Protocol.Http
        LocalPort = 8888
        PublicPort = None }
      { ServiceMetadata.ServiceName = "postgres"
        ProjectName = Some "mdk-200"
        Host = "postgres.mdk-200.local"
        Mode = Protocol.Tcp
        LocalPort = 5432
        PublicPort = None } ]

  let output = Rendering.render services

  Assert.Contains("frontend stackproxy_http", output)
  Assert.Contains("backend http_moneydevkit_mdk_200_local", output)
  Assert.Contains("frontend stackproxy_tcp", output)
  Assert.Contains("backend tcp_postgres_mdk_200_local", output)
