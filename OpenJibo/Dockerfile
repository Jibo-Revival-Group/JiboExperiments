FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["OpenJibo.slnx", "./"]
COPY ["src/Jibo.Runtime.Abstractions/Jibo.Runtime.Abstractions.csproj", "src/Jibo.Runtime.Abstractions/"]
COPY ["src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Domain/Jibo.Cloud.Domain.csproj", "src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Domain/"]
COPY ["src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Application/Jibo.Cloud.Application.csproj", "src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Application/"]
COPY ["src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Infrastructure/Jibo.Cloud.Infrastructure.csproj", "src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Infrastructure/"]
COPY ["src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Api/Jibo.Cloud.Api.csproj", "src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Api/"]

RUN dotnet restore "src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Api/Jibo.Cloud.Api.csproj"

COPY . .
RUN dotnet publish "src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Api/Jibo.Cloud.Api.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Jibo.Cloud.Api.dll"]
