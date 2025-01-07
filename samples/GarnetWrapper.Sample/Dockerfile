FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["samples/GarnetWrapper.Sample/GarnetWrapper.Sample.csproj", "samples/GarnetWrapper.Sample/"]
COPY ["src/GarnetWrapper/GarnetWrapper.csproj", "src/GarnetWrapper/"]
RUN dotnet restore "samples/GarnetWrapper.Sample/GarnetWrapper.Sample.csproj"
COPY . .
WORKDIR "/src/samples/GarnetWrapper.Sample"
RUN dotnet build "GarnetWrapper.Sample.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "GarnetWrapper.Sample.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GarnetWrapper.Sample.dll"] 