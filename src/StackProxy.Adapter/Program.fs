namespace StackProxy.Adapter

open System
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks

module Program =
  let private startLoop (reader: ChannelReader<Reconciler.ServiceEvent>) (writeConfig: ServiceMetadata list -> unit) (ct: CancellationToken) =
    let rec loop state = task {
      let! hasItem = reader.WaitToReadAsync(ct).AsTask()
      if not hasItem then
        return ()
      else
        let! evt = reader.ReadAsync(ct).AsTask()
        let nextState = Reconciler.applyEvents writeConfig state [ evt ]
        return! loop nextState
    }

    loop ServiceRegistry.empty

  [<EntryPoint>]
  let main _argv =
    let settings = AdapterSettings.fromProcessEnv()
    use docker = DockerClient.create settings
    use cts = new CancellationTokenSource()

    Console.CancelKeyPress.Add(fun args ->
      args.Cancel <- true
      cts.Cancel())

    let run = task {
      let! reader = DockerWatcher.watch docker settings cts.Token
      let pidFile = Environment.GetEnvironmentVariable("STACK_PROXY_HAPROXY_PID") |> Option.ofObj
      let writeConfig services = ConfigWriter.writeConfig settings.ConfigPath pidFile services
      do! startLoop reader writeConfig cts.Token
    }

    try
      run.GetAwaiter().GetResult()
      0
    with :? OperationCanceledException -> 0
