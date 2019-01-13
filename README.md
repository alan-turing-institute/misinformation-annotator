# Misinformation annotator tool

Using the F# SAFE stack - work in progress

See [main project](https://github.com/alan-turing-institute/misinformation) for project board and issues.

## Requirements

- [Mono](http://www.mono-project.com/) on MacOS/Linux
- [.NET Framework 4.6.2](https://support.microsoft.com/en-us/help/3151800/the--net-framework-4-6-2-offline-installer-for-windows) on Windows
- [node.js](https://nodejs.org/) - JavaScript runtime
- [yarn](https://yarnpkg.com/) - Package manager for npm modules
- [dotnet SDK 2.1.302](https://github.com/dotnet/core/blob/master/release-notes/download-archives/2.1.1-download.md) The .NET Core SDK
- For [deployment](#deployment) you need to have [docker](https://www.docker.com/) installed.

## Development mode

The SAFE development stack is designed to be used with minimal tooling. An instance of Visual Studio Code together with the  [Ionide](http://ionide.io/) plugin should be enough.

Start the development mode with:

    > build.cmd run // on windows
    $ ./build.sh run // on unix

This command will call the target "Run" in **build.fsx**. This will start in parallel:
- **dotnet fable webpack-dev-server** in [src/Client](src/Client) (note: the Webpack development server will serve files on http://localhost:8080)
- **dotnet watch msbuild /t:TestAndRun** in [test/serverTests](src/ServerTests) to run unit tests and then server (note: Giraffe is launched on port **8085**)

You can now edit files in `src/Server` or `src/Client` and recompile + browser refresh will be triggered automatically.
For the case of the client ["Hot Module Replacement"](https://webpack.js.org/concepts/hot-module-replacement/) is supported, which means your app state is kept over recompile.

Usually you can just keep this mode running and running. Just edit files, see the browser refreshing and commit + push with git.

In development mode, the app runs locally using files from local file system. You can change that by providing it with the Azure connection strings in environment variables for blob storage `CUSTOMCONNSTR_BLOBSTORAGE` and SQL database `SQLAZURECONNSTR_SQLDATABASE`. When the environment variables are set, the app runs locally while using data from Azure storage.

## Deployment

The deployment for this repo works via [docker](https://www.docker.com/) with Linux containers and therefore you need docker installed on your machine. The deployment is automatic to Azure from Docker Hub.

#### Docker Hub

The DockerHub repository for the project is [turinginst/misinformation](https://cloud.docker.com/u/turinginst/repository/docker/turinginst/misinformation).

#### Release script

To deploy the app, copy the following content to a `release.sh` script and fill in your DockerHub credentials:
    
    #!/usr/bin/env bash
    set -eu
    cd "$(dirname "$0")"

    PAKET_EXE=.paket/paket.exe
    FAKE_EXE=packages/build/FAKE/tools/FAKE.exe

    FSIARGS=""
    FSIARGS2=""
    OS=${OS:-"unknown"}
    if [ "$OS" != "Windows_NT" ]
    then
      FSIARGS="--fsiargs"
      FSIARGS2="-d:MONO"
    fi

    run() {
      if [ "$OS" != "Windows_NT" ]
      then
        mono "$@"
      else
        "$@"
      fi
    }

    DOCKERARGS="Deploy DockerLoginServer=docker.io DockerImageName=misinformation DockerOrg=turinginst DockerUser=CHANGE_ME  DockerPassword=CHANGE_ME"
    run $PAKET_EXE restore
    run $FAKE_EXE $DOCKERARGS $FSIARGS $FSIARGS2 build.fsx

Don't worry the file is already in `.gitignore` so your password will not be commited.

#### Docker push

In order to release a container you need to create a new entry in `RELEASE_NOTES.md` and run `release.sh`.
This will build the server and client, run all test, put the app into a docker container and push it to your docker hub repo.
This triggers a webhook for Azure, which deploys the app.

You should be able to reach the website on [misinformation.azurewebsites.net](http://misinformation.azurewebsites.net).

#### Notes

- The app is deployed using the Azure *Web app for containers* resource
- The webhook URL in Azure container settings is used in the Docker hub repo
- For additional information see [SAFE BookStore app](https://github.com/SAFE-Stack/SAFE-BookStore)


## Using the website

For testing purposes, you can log into the website using `test`/`test` credentials.
