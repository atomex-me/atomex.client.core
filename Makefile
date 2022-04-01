install:
	dotnet restore

test:
	dotnet test -v d Atomex.Client.Core.Tests