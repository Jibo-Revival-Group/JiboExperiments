FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["OpenJibo.slnx", "./"]
COPY ["src/Jibo.Runtime.Abstractions/Jibo.Runtime.Abstractions.csproj", "src/Jibo.Runtime.Abstractions/"]
COPY ["src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Domain/Jibo.Cloud.Domain.csproj", "src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Domain/"]
COPY ["src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Application/Jibo.Cloud.Application.csproj", "src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Application/"]
COPY ["src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Infrastructure/Jibo.Cloud.Infrastructure.csproj", "src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Infrastructure/"]
COPY ["src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Api/Jibo.Cloud.Api.csproj", "src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Api/"]
COPY ["src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Migrations/Jibo.Cloud.Migrations.csproj", "src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Migrations/"]

RUN dotnet restore "src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Api/Jibo.Cloud.Api.csproj"
RUN dotnet restore "src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Migrations/Jibo.Cloud.Migrations.csproj"

COPY . .
RUN dotnet publish "src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Api/Jibo.Cloud.Api.csproj" \
    -c Release \
    -o /app/publish/api \
    /p:UseAppHost=false
RUN dotnet publish "src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Migrations/Jibo.Cloud.Migrations.csproj" \
    -c Release \
    -o /app/publish/migrations \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish/api ./api
COPY --from=build /app/publish/migrations ./migrations
ENTRYPOINT ["dotnet"]
CMD ["/app/api/Jibo.Cloud.Api.dll"]
