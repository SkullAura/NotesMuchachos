FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ProjectCal.slnx ./
COPY src/ProjectCal.Shared/ProjectCal.Shared.csproj src/ProjectCal.Shared/
COPY src/ProjectCal.Api/ProjectCal.Api.csproj src/ProjectCal.Api/
COPY src/ProjectCal.Worker/ProjectCal.Worker.csproj src/ProjectCal.Worker/
RUN dotnet restore src/ProjectCal.Worker/ProjectCal.Worker.csproj
COPY src/ProjectCal.Shared src/ProjectCal.Shared
COPY src/ProjectCal.Api src/ProjectCal.Api
COPY src/ProjectCal.Worker src/ProjectCal.Worker
RUN dotnet publish src/ProjectCal.Worker/ProjectCal.Worker.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ProjectCal.Worker.dll"]
