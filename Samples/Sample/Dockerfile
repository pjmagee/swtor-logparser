FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Samples/Sample/Sample.csproj", "Samples/Sample/"]
RUN dotnet restore "Samples/Sample/Sample.csproj"
COPY . .
WORKDIR "/src/Samples/Sample"
RUN dotnet build "Sample.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Sample.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Sample.dll"]
