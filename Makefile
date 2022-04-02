install:
	dotnet restore

build:
	dotnet build

test:
	dotnet test -v d Atomex.Client.Core.Tests