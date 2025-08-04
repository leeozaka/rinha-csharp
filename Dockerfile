FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build

RUN apk add --no-cache clang build-base zlib-dev

WORKDIR /src
COPY . .

RUN dotnet restore
RUN dotnet publish Rinha-Csharp/Rinha-Csharp.csproj -c Release -o /app

#FROM mcr.microsoft.com/dotnet/runtime-deps:9.0-alpine AS runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime

WORKDIR /app
COPY --from=build /app .

ENTRYPOINT ["./Rinha-Csharp"]
