FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY DistributedRank.csproj ./
COPY Program.cs ./
COPY Models/ ./Models/
COPY Network/ ./Network/
COPY Core/ ./Core/
COPY Nodes/ ./Nodes/
COPY Helpers/ ./Helpers/
RUN dotnet publish DistributedRank.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app/publish ./
COPY data ./data
ENTRYPOINT ["dotnet", "DistributedRank.dll"]
