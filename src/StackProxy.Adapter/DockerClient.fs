namespace StackProxy.Adapter

open System
open System.Threading.Tasks
open Docker.DotNet
open Docker.DotNet.Models

module DockerClient =
  type ContainerSnapshot =
    { Id: string
      Metadata: ServiceMetadata }

  let create (settings: AdapterSettings) : IDockerClient =
    new DockerClientConfiguration(Uri settings.DockerUri) 
    |> fun cfg -> cfg.CreateClient()

  let private isOnNetwork (settings: AdapterSettings) (response: ContainerListResponse) =
    if isNull response.NetworkSettings || isNull response.NetworkSettings.Networks then
      false
    else
      response.NetworkSettings.Networks.ContainsKey(settings.NetworkName)

  let private trySnapshot (settings: AdapterSettings) (response: ContainerListResponse) : ContainerSnapshot option =
    if isOnNetwork settings response then
      let info = ContainerInfo.fromListResponse response
      ContainerInfo.toRaw info
      |> Metadata.tryCreate
      |> Option.map (fun meta -> { Id = response.ID; Metadata = meta })
    else
      None

  let listServices (client: IDockerClient) (settings: AdapterSettings) : Task<ContainerSnapshot list> = task {
    let parameters = ContainersListParameters(All = true)
    let! containers = client.Containers.ListContainersAsync(parameters)
    return
      containers
      |> Seq.choose (trySnapshot settings)
      |> Seq.toList
  }
