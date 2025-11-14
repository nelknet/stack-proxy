namespace StackProxy.Adapter

open System
open System.IO

open System.Diagnostics

module ConfigWriter =
  let private ensureDirectory (path: string) =
    let directory = Path.GetDirectoryName(path)
    if String.IsNullOrWhiteSpace(directory) |> not then
      Directory.CreateDirectory(directory) |> ignore

  let private writeTempFile targetPath content =
    let tempPath = targetPath + $".tmp.{Guid.NewGuid():N}"
    File.WriteAllText(tempPath, content)
    tempPath

  let private tryReload (haproxyPidFile: string option) (configPath: string) =
    match haproxyPidFile with
    | None -> ()
    | Some pidFile when File.Exists pidFile ->
        let pidText = File.ReadAllText(pidFile).Trim()
        match Int32.TryParse pidText with
        | true, pid ->
            Process.Start( ProcessStartInfo("haproxy", $"-f {configPath} -p {pidFile} -sf {pid}") ) |> ignore
        | _ -> ()
    | _ -> ()

  let writeConfig (targetPath: string) (haproxyPidFile: string option) (services: ServiceMetadata list) =
    ensureDirectory targetPath
    let rendered = Rendering.render services
    let tempPath = writeTempFile targetPath rendered
    File.Move(tempPath, targetPath, true)
    tryReload haproxyPidFile targetPath
