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

let private rawWithNames service project labelPairs ports names =
  { RawServiceInput.ServiceName = service
    ProjectName = project
    Labels = labels labelPairs
    ExposedPorts = ports
    ContainerNames = names }

let private raw service project labelPairs ports =
  rawWithNames service project labelPairs ports [ service ]

let private getMetadata input =
  match Metadata.tryCreate input with
  | Some meta -> meta
  | None -> failwith "Expected metadata, got None"

[<Fact>]
let ``falls back to project-based host and exposed port`` () =
  let input = raw "moneydevkit" (Some "mdk-100") [] [ 8888 ]
  let meta = getMetadata input
  Assert.Equal("moneydevkit.mdk-100.localhost", meta.Host)
  Assert.Equal(8888, meta.LocalPort)
  Assert.Equal(Protocol.Http, meta.Mode)

[<Fact>]
let ``respects explicit host and local port labels`` () =
  let input =
    raw
      "moneydevkit"
      (Some "mdk-100")
      [ ("stack-proxy.host", "custom.host.localhost"); ("stack-proxy.localport", "3900") ]
      []
  let meta = getMetadata input
  Assert.Equal("custom.host.localhost", meta.Host)
  Assert.Equal(3900, meta.LocalPort)

[<Fact>]
let ``uses container name when available`` () =
  let input = rawWithNames "web" (Some "mdk-102") [] [ 3000 ] [ "test1-web-1" ]
  let meta = getMetadata input
  Assert.Equal("web.mdk-102.localhost", meta.Host)
  Assert.Equal("test1-web-1", meta.ContainerAddress)
  let route = Routing.describe meta
  Assert.Equal("test1-web-1:3000", route.ServerAddress)

[<Fact>]
let ``infers tcp mode and default port when none exposed`` () =
  let input =
    raw
      "postgres"
      (Some "mdk-101")
      [ ("stack-proxy.mode", "tcp") ]
      []
  let meta = getMetadata input
  Assert.Equal(Protocol.Tcp, meta.Mode)
  Assert.Equal(5432, meta.LocalPort)

[<Fact>]
let ``returns none when disabled label present`` () =
  let input = raw "admin" None [ ("stack-proxy.disable", "true") ] [ 3000 ]
  let result = Metadata.tryCreate input
  Assert.True(result.IsNone)

[<Fact>]
let ``routing descriptor normalizes host names`` () =
  let meta =
    { ServiceMetadata.ServiceName = "moneydevkit.com"
      ProjectName = Some "mdk-200"
      Host = "MoneyDevkit-APP.mdk-200.localhost"
      Mode = Protocol.Http
      LocalPort = 8888
      PublicPort = None
      ContainerAddress = "moneydevkit.com" }

  let route = Routing.describe meta
  Assert.Equal("http_moneydevkit_app_mdk_200_localhost", route.BackendName)
  Assert.Equal("moneydevkit.com:8888", route.ServerAddress)

[<Fact>]
let ``tcp routing descriptor uses tcp prefix`` () =
  let meta =
    { ServiceMetadata.ServiceName = "postgres"
      ProjectName = Some "mdk-201"
      Host = "postgres.mdk-201.localhost"
      Mode = Protocol.Tcp
      LocalPort = 5432
      PublicPort = None
      ContainerAddress = "postgres" }

  let route = Routing.describe meta
  Assert.Equal("tcp_postgres_mdk_201_localhost", route.BackendName)
  Assert.Equal("postgres:5432", route.ServerAddress)

[<Fact>]
let ``renders http and tcp sections`` () =
  let services =
    [ { ServiceMetadata.ServiceName = "moneydevkit.com"
        ProjectName = Some "mdk-200"
        Host = "moneydevkit.mdk-200.localhost"
        Mode = Protocol.Http
        LocalPort = 8888
        PublicPort = None
        ContainerAddress = "moneydevkit.com" }
      { ServiceMetadata.ServiceName = "postgres"
        ProjectName = Some "mdk-200"
        Host = "postgres.mdk-200.localhost"
        Mode = Protocol.Tcp
        LocalPort = 5432
        PublicPort = None
        ContainerAddress = "postgres" } ]

  let output = Rendering.render services

  Assert.Contains("frontend stackproxy_http", output)
  Assert.Contains("backend http_moneydevkit_mdk_200_localhost", output)
  Assert.Contains("frontend stackproxy_tcp", output)
  Assert.Contains("backend tcp_postgres_mdk_200_localhost", output)

[<Fact>]
let ``writes rendered config atomically`` () =
  let services =
    [ { ServiceMetadata.ServiceName = "moneydevkit.com"
        ProjectName = Some "mdk-300"
        Host = "moneydevkit.mdk-300.localhost"
        Mode = Protocol.Http
        LocalPort = 8888
        PublicPort = None
        ContainerAddress = "moneydevkit.com" } ]

  let tempDir = Path.Combine(Path.GetTempPath(), "stack-proxy-tests", Guid.NewGuid().ToString("N"))
  Directory.CreateDirectory(tempDir) |> ignore
  let targetPath = Path.Combine(tempDir, "haproxy.cfg")

  ConfigWriter.writeConfig targetPath None services
  let first = File.ReadAllText(targetPath)
  ConfigWriter.writeConfig targetPath None services
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
  Assert.Equal(5432, settings.TcpPort)
  Assert.Equal("stack-proxy", settings.LabelPrefix)
  Assert.Equal("stack-proxy", settings.NetworkName)

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
    PublicPort = None
    ContainerAddress = name }

[<Fact>]
let ``service registry upserts and removes`` () =
  let m1 = mkMeta "moneydevkit.com" "moneydevkit.localhost" Protocol.Http 8888
  let m2 = mkMeta "admin" "admin.localhost" Protocol.Http 3000

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

  let meta = mkMeta "moneydevkit.com" "moneydevkit.localhost" Protocol.Http 8888
  let events = [ Reconciler.Upsert("container1", meta) ]

  let nextState = Reconciler.applyEvents writer ServiceRegistry.empty events

  Assert.Equal(1, writes.Count)
  Assert.Equal("moneydevkit.localhost", writes[0] |> List.head |> fun s -> s.Host)

  let events2 = [ Reconciler.Remove "container1" ]
  let finalState = Reconciler.applyEvents writer nextState events2

  Assert.Equal(2, writes.Count)
  Assert.Empty(ServiceRegistry.asList finalState)
