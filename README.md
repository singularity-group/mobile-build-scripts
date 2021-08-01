# mobile-build-scripts

Mobile build scripts for modern game dev.

## Features

- Depend on cocoapods for iOS.
- Iterate on Android plugins code in Android Studio (by building gradle modules during Unity build)

## Getting Started

Warning:

- this project contains Google's PlayServicesResolver, which you may already have in your Unity project. So if you're already using Google's PlayServicesResolver, you'll need to remove your copy of it.
- the TODO section contains a list of known issues

Add to your Unity project's `manifest.json`:

```json
"com.gamingforgood.mobile_build_scripts": "https://github.com/singularity-group/mobile-build-scripts.git?path=/Packages/com.gamingforgood.mobile_build_scripts#main",
```

### Depend on Cocoapods

1. Make a file called `MyIosDependencies.yml`

```yml
# Depend on the UnityAds cocoapod
UnityAds:
  version: 3.2
```

2. Build and Run for iOS.
3. The above depends on the UnityAds cocoapod.

Many kinds of dependencies are supported.

**Git repository**

```yml
HaishinKit:
  git: https://github.com/shogo4405/HaishinKit.swift
  commit: 3f3799482f
```

**Path from repository root**

Your Unity project is part of a git repo, and you have a cocoapod somewhere in the same repo.

```yml
Commons:
  path: MobilePlugins/NativeSources/iOS/Commons
```

In this example, the folder 'MobilePlugins/NativeSources/iOS/Commons/' contains a `podspec` file.

**More**

- For examples of all syntax, see: [ExampleIosDependencies.yml](Packages/com.gamingforgood.mobile_build_scripts/Editor/IosDependencyResolver/ExampleIosDependencies.yml)
- Each YAML filename must end with `IosDependencies.yml` and you can place anything before this.
- [Source code here](Packages/com.gamingforgood.mobile_build_scripts/Editor/IosDependencyResolver/IosDependencyResolver.cs)

## Contributing

These scripts are used in production for Clash of Streamers, and might work for your project too.

Feedback and PRs are welcomed. If you will take on a big feature, make an issue to discuss before starting.

## TODO

- `'GoogleUtilities'` must not be included in every Podfile! (make it optional or move to other package)
- Remove code that is no longer needed - see comments in the code.
- Publish to NPM?
- support git tags?
- publish unity-jar-resolver as a Unity package OR fill all useful features of unity-jar-resolver and remove it