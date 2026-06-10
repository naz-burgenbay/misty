FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /repo

COPY Misty.slnx ./
COPY src/Misty.Domain/Misty.Domain.csproj            src/Misty.Domain/
COPY src/Misty.Application/Misty.Application.csproj  src/Misty.Application/
COPY src/Misty.Infrastructure/Misty.Infrastructure.csproj src/Misty.Infrastructure/
COPY src/Misty.Api/Misty.Api.csproj                  src/Misty.Api/

RUN dotnet restore src/Misty.Api/Misty.Api.csproj

COPY src/ src/
RUN dotnet publish src/Misty.Api/Misty.Api.csproj \
    -c Release \
    --no-restore \
    -o /app/publish


FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

RUN addgroup --system --gid 1001 misty \
 && adduser  --system --uid 1001 --ingroup misty --shell /bin/false misty

COPY --from=build --chown=misty:misty /app/publish ./

USER misty

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Misty.Api.dll"]
