all: debug release

debug:
	export FrameworkPathOverride=/usr/lib/mono/4.7-api/ && dotnet build --configuration Debug mapf.csproj

release:
	export FrameworkPathOverride=/usr/lib/mono/4.7-api/ && dotnet build --configuration Release mapf.csproj

clean:
	dotnet clean mapf.csproj
	dotnet clean --configuration Release mapf.csproj