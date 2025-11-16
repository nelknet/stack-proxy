namespace StackProxy.Adapter

open System
open System.Text

module Rendering =
  let private appendLine (sb: StringBuilder) (text: string) =
    sb.AppendLine(text) |> ignore

  let private renderHttp (sb: StringBuilder) (routes: Routing.RouteDefinition list) =
    if routes.IsEmpty then () else
      appendLine sb "frontend stackproxy_http"
      appendLine sb "  bind *:80"
      appendLine sb "  mode http"
      appendLine sb "  option httplog"
      for route in routes do
        appendLine sb $"  acl host_{route.BackendName} hdr(host) -i {route.HostMatch}"
      for route in routes do
        appendLine sb $"  use_backend {route.BackendName} if host_{route.BackendName}"
      appendLine sb "  default_backend stackproxy_http_fallback"
      appendLine sb ""
      appendLine sb "backend stackproxy_http_fallback"
      appendLine sb "  mode http"
      appendLine sb "  http-request deny deny_status 404"
      appendLine sb ""
      for route in routes do
        appendLine sb $"backend {route.BackendName}"
        appendLine sb "  mode http"
        appendLine sb "  balance roundrobin"
        appendLine sb $"  server {route.BackendName}_srv {route.ServerAddress} check"
        appendLine sb ""

  let private renderTcp (sb: StringBuilder) (routes: Routing.RouteDefinition list) =
    if routes.IsEmpty then () else
      appendLine sb "frontend stackproxy_tcp"
      appendLine sb "  bind *:5432"
      appendLine sb "  mode tcp"
      appendLine sb "  tcp-request inspect-delay 5s"
      appendLine sb "  tcp-request content accept if WAIT_END"
      for route in routes do
        appendLine sb $"  acl sni_{route.BackendName} req.ssl_sni -i {route.HostMatch}"
      for route in routes do
        appendLine sb $"  use_backend {route.BackendName} if sni_{route.BackendName}"
      appendLine sb "  default_backend stackproxy_tcp_fallback"
      appendLine sb ""
      appendLine sb "backend stackproxy_tcp_fallback"
      appendLine sb "  mode tcp"
      appendLine sb "  tcp-request content reject"
      appendLine sb ""
      for route in routes do
        appendLine sb $"backend {route.BackendName}"
        appendLine sb "  mode tcp"
        appendLine sb "  balance roundrobin"
        appendLine sb $"  server {route.BackendName}_srv {route.ServerAddress} check"
        appendLine sb ""

  let render (services: ServiceMetadata list) =
    let sb = StringBuilder()
    let routes = services |> List.map Routing.describe
    let httpRoutes = routes |> List.filter (fun route -> route.Mode = Protocol.Http)
    let tcpRoutes = routes |> List.filter (fun route -> route.Mode = Protocol.Tcp)

    renderHttp sb httpRoutes
    renderTcp sb tcpRoutes

    let rendered = sb.ToString().Trim()
    if String.IsNullOrWhiteSpace rendered then rendered else rendered + Environment.NewLine
