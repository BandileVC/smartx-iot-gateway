FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["SmartX.Simulator/SmartX.Simulator.csproj", "SmartX.Simulator/"]
COPY ["SmartX.Shared/SmartX.Shared.csproj", "SmartX.Shared/"]
RUN dotnet restore "SmartX.Simulator/SmartX.Simulator.csproj"
COPY . .
WORKDIR /src/SmartX.Simulator
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SmartX.Simulator.dll"]