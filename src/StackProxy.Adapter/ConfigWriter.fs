namespace StackProxy.Adapter

open System
open System.IO

module ConfigWriter =
  let private ensureDirectory (path: string) =
    let directory = Path.GetDirectoryName(path)
    if String.IsNullOrWhiteSpace(directory) |> not then
      Directory.CreateDirectory(directory) |> ignore

  let private writeTempFile targetPath content =
    let tempPath = targetPath + $".tmp.{Guid.NewGuid():N}"
    File.WriteAllText(tempPath, content)
    tempPath

  let writeConfig (targetPath: string) (services: ServiceMetadata list) =
    ensureDirectory targetPath
    let rendered = Rendering.render services
    let tempPath = writeTempFile targetPath rendered
    File.Move(tempPath, targetPath, true)
