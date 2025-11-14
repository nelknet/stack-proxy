module StackProxy.Adapter.Tests.MetadataTests

open System
open System.Collections.Generic
open System.IO
open Xunit
open Docker.DotNet.Models
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

[<Fact>]
let ``writes rendered config atomically`` () =
  let services =
    [ { ServiceMetadata.ServiceName = "moneydevkit.com"
        ProjectName = Some "mdk-300"
        Host = "moneydevkit.mdk-300.local"
        Mode = Protocol.Http
        LocalPort = 8888
        PublicPort = None } ]

  let tempDir = Path.Combine(Path.GetTempPath(), "stack-proxy-tests", Guid.NewGuid().ToString("N"))
  Directory.CreateDirectory(tempDir) |> ignore
  let targetPath = Path.Combine(tempDir, "haproxy.cfg")

  ConfigWriter.writeConfig targetPath services
  let first = File.ReadAllText(targetPath)
  ConfigWriter.writeConfig targetPath services
  let second = File.ReadAllText(targetPath)

  Assert.True(File.Exists(targetPath))
  Assert.Equal(first, second)
  Assert.Contains("frontend stackproxy_http", second)

[<Fact>]
let ``settings fall back to defaults`` () =
  let env _ = None
  let settings = AdapterSettings.fromEnv env
  Assert.Equal("unix:///var/run/docker.sock", settings.DockerUri)
  Assert.Equal("/etc/haproxy/generated.cfg", settings.ConfigPath)
  Assert.Equal(80, settings.HttpPort)
  Assert.Equal(15432, settings.TcpPort)
  Assert.Equal("mdk", settings.LabelPrefix)
  Assert.Equal("proxy", settings.NetworkName)

[<Fact>]
let ``settings parse overrides`` () =
  let env key =
    match key with
    | "STACK_PROXY_DOCKER_URI" -> Some "npipe://./pipe/docker_engine"
    | "STACK_PROXY_CONFIG_PATH" -> Some "/tmp/haproxy.cfg"
    | "STACK_PROXY_HTTP_PORT" -> Some "8080"
    | "STACK_PROXY_TCP_PORT" -> Some "16000"
    | "STACK_PROXY_LABEL_PREFIX" -> Some "custom"
    | "STACK_PROXY_NETWORK" -> Some "mdk-proxy"
    | _ -> None

  let settings = AdapterSettings.fromEnv env
  Assert.Equal("npipe://./pipe/docker_engine", settings.DockerUri)
  Assert.Equal("/tmp/haproxy.cfg", settings.ConfigPath)
  Assert.Equal(8080, settings.HttpPort)
  Assert.Equal(16000, settings.TcpPort)
  Assert.Equal("custom", settings.LabelPrefix)
  Assert.Equal("mdk-proxy", settings.NetworkName)

[<Fact>]
let ``docker container info prefers compose labels`` () =
  let info =
    { ContainerInfo.Names = [ "/stack-service-1" ]
      Labels =
        labels
          [ "com.docker.compose.service", "moneydevkit.com"
            "com.docker.compose.project", "mdk-400" ]
      ExposedPorts = [ 8888 ] }

  let raw = ContainerInfo.toRaw info
  Assert.Equal("moneydevkit.com", raw.ServiceName)
  Assert.Equal<string option>(Some "mdk-400", raw.ProjectName)
  Assert.Equal<int list>([ 8888 ], raw.ExposedPorts)

[<Fact>]
let ``docker container info falls back to name`` () =
  let info =
    { ContainerInfo.Names = [ "/postgres" ]
      Labels = labels []
      ExposedPorts = [] }

  let raw = ContainerInfo.toRaw info
  Assert.Equal("postgres", raw.ServiceName)
  Assert.True(raw.ProjectName.IsNone)

[<Fact>]
let ``converts docker list response to container info`` () =
  let response = ContainerListResponse()
  response.Names <- ResizeArray<string>([| "/svc" |])
  response.Labels <- dict [ "com.docker.compose.service", "svc" ]
  response.Ports <- ResizeArray<Port>([| Port(PrivatePort = 8080us) |])

  let info = ContainerInfo.fromListResponse response
  Assert.Equal<string list>([ "svc" ], info.Names)
  Assert.Equal<int list>([ 8080 ], info.ExposedPorts)

[<Fact>]
let ``converts inspect response to container info`` () =
  let inspect = ContainerInspectResponse()
  inspect.Name <- "/svc"
  inspect.Config <- Config()
  inspect.Config.Labels <- dict [ "com.docker.compose.project", "mdk-500" ]
  inspect.NetworkSettings <- NetworkSettings()
  let ports = dict [ "8888/tcp", null :> IList<PortBinding> ]
  inspect.NetworkSettings.Ports <- ports

  let info = ContainerInfo.fromInspectResponse inspect
  Assert.Equal<string list>([ "svc" ], info.Names)
  Assert.Equal<int list>([ 8888 ], info.ExposedPorts)

let private mkMeta name host mode port =
  { ServiceMetadata.ServiceName = name
    ProjectName = None
    Host = host
    Mode = mode
    LocalPort = port
    PublicPort = None }

[<Fact>]
let ``service registry upserts and removes`` () =
  let m1 = mkMeta "moneydevkit.com" "moneydevkit.local" Protocol.Http 8888
  let m2 = mkMeta "admin" "admin.local" Protocol.Http 3000

  let state =
    ServiceRegistry.empty
    |> ServiceRegistry.upsert "container1" m1
    |> ServiceRegistry.upsert "container2" m2

  let names =
    state
    |> ServiceRegistry.asList
    |> List.map (fun meta -> meta.ServiceName)
    |> List.sort

  Assert.Equal<string list>([ "admin"; "moneydevkit.com" ], names)

  let state' = ServiceRegistry.remove "container1" state
  let remaining = state' |> ServiceRegistry.asList
  let only = Assert.Single(remaining)
  Assert.Equal("admin", only.ServiceName)

[<Fact>]
let ``reconciler applies events and writes config`` () =
  let writes = ResizeArray<ServiceMetadata list>()
  let writer services = writes.Add(services)

  let meta = mkMeta "moneydevkit.com" "moneydevkit.local" Protocol.Http 8888
  let events = [ Reconciler.Upsert("container1", meta) ]

  let nextState = Reconciler.applyEvents writer ServiceRegistry.empty events

  Assert.Equal(1, writes.Count)
  Assert.Equal("moneydevkit.local", writes[0] |> List.head |> fun s -> s.Host)

  let events2 = [ Reconciler.Remove "container1" ]
  let finalState = Reconciler.applyEvents writer nextState events2

  Assert.Equal(2, writes.Count)
  Assert.Empty(ServiceRegistry.asList finalState)
