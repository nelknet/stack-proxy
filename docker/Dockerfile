# syntax=docker/dockerfile:1.6

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/StackProxy.Adapter/StackProxy.Adapter.fsproj -c Release -o /out

FROM haproxy:3.0-alpine
USER root
RUN apk add --no-cache bash dotnet8-runtime
COPY docker/haproxy/base.cfg /etc/haproxy/base.cfg
COPY docker/haproxy/generated.cfg /etc/haproxy/generated.cfg
COPY docker/entrypoint.sh /entrypoint.sh
COPY --from=build /out /app
ENTRYPOINT ["/entrypoint.sh"]
