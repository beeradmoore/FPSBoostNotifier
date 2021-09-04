#!/bin/sh

pushd FPSBoostNotifier
    dotnet lambda deploy-function --profile beeradmoore
popd 