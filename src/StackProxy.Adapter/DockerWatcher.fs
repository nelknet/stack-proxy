namespace StackProxy.Adapter

open System
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Docker.DotNet
open Docker.DotNet.Models

module DockerWatcher =
  let private isRelevant action =
    match action with
    | "start" | "restart" | "update" | "die" | "stop" | "destroy" -> true
    | _ -> false

  let private metadataFromInspect settings (inspect: ContainerInspectResponse) =
    ContainerInfo.fromInspectResponse inspect
    |> ContainerInfo.toRaw
    |> Metadata.tryCreate

  let private handleMessage (client: IDockerClient) settings ct (writer: ChannelWriter<Reconciler.ServiceEvent>) (message: Message) =
    task {
      if message.Type = "container" && isRelevant message.Action then
        match message.Action with
        | "die" | "stop" | "destroy" ->
          do! writer.WriteAsync(Reconciler.Remove message.ID, ct).AsTask()
        | _ ->
          try
            let! inspect = client.Containers.InspectContainerAsync(message.ID, ct)
            match metadataFromInspect settings inspect with
            | Some metadata ->
              do! writer.WriteAsync(Reconciler.Upsert(message.ID, metadata), ct).AsTask()
            | None -> ()
          with _ -> ()
    }

  let watch (client: IDockerClient) (settings: AdapterSettings) (ct: CancellationToken) : Task<ChannelReader<Reconciler.ServiceEvent>> =
    task {
      let channel = Channel.CreateUnbounded<Reconciler.ServiceEvent>()
      let writer = channel.Writer

      let! snapshots = DockerClient.listServices client settings
      for snapshot in snapshots do
        do! writer.WriteAsync(Reconciler.Upsert(snapshot.Id, snapshot.Metadata), ct).AsTask()

      let progress =
        { new IProgress<Message> with
            member _.Report(message) =
              let _ = handleMessage client settings ct writer message in () }

      let parameters = ContainerEventsParameters()
      let monitorTask = client.System.MonitorEventsAsync(parameters, progress, ct)

      let _ =
        monitorTask.ContinueWith(
          (fun (t: Task) ->
            if t.IsFaulted then
              writer.TryComplete(t.Exception) |> ignore
            else
              writer.TryComplete() |> ignore),
          TaskScheduler.Default)

      return channel.Reader
    }
