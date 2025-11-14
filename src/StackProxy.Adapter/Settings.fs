namespace StackProxy.Adapter

open System

[<CLIMutable>]
type AdapterSettings =
  { DockerUri: string
    ConfigPath: string
    HttpPort: int
    TcpPort: int
    LabelPrefix: string
    NetworkName: string }

module AdapterSettings =
  let private defaultHttpPort = 80
  let private defaultTcpPort = 15432
  let private defaultDockerUri = "unix:///var/run/docker.sock"
  let private defaultConfigPath = "/etc/haproxy/generated.cfg"
  let private defaultLabelPrefix = "mdk"
  let private defaultNetwork = "proxy"

  let private parseInt fallback (value: string option) =
    match value with
    | Some str when Int32.TryParse str |> fst -> Int32.Parse str
    | _ -> fallback

  let fromEnv (getEnv: string -> string option) =
    { DockerUri = getEnv "STACK_PROXY_DOCKER_URI" |> Option.defaultValue defaultDockerUri
      ConfigPath = getEnv "STACK_PROXY_CONFIG_PATH" |> Option.defaultValue defaultConfigPath
      HttpPort = getEnv "STACK_PROXY_HTTP_PORT" |> parseInt defaultHttpPort
      TcpPort = getEnv "STACK_PROXY_TCP_PORT" |> parseInt defaultTcpPort
      LabelPrefix = getEnv "STACK_PROXY_LABEL_PREFIX" |> Option.defaultValue defaultLabelPrefix
      NetworkName = getEnv "STACK_PROXY_NETWORK" |> Option.defaultValue defaultNetwork }

  let fromProcessEnv () =
    let getter key =
      match Environment.GetEnvironmentVariable(key) with
      | null -> None
      | value when String.IsNullOrWhiteSpace value -> None
      | value -> Some value
    fromEnv getter
