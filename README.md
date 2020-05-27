# Shim

A small program that calls the executable configured in a `<name of itself>.shim` file.

This is a helper program for [Scoop](https://scoop.sh), the Windows command-line installer.

# Shim File Format
```
path = <path to executable without quotes>
args = <arguments>
env:VARNAME = <value>
envappend:VARNAME = <value>
envprepend:VARNAME = <value>
envsep = <value>
envclear:VARNAME = <anything>
```

# Usage
The `*.shim` file must have the same name as the `shim.exe`.

```pwsh
New-Item -Path test.shim -Value 'path = C:\Windows\System32\calc.exe'
Copy-Item -Path .\build\shim.exe -Destination .\test.exe
.\test.exe
```
