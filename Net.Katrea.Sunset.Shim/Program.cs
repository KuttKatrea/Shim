using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Net.Katrea.Sunset.Shim
{
  internal class Program
  {
    private const int ERROR_ELEVATION_REQUIRED = 740;
    private const uint INFINITE = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(string lpApplicationName,
      string lpCommandLine, IntPtr lpProcessAttributes,
      IntPtr lpThreadAttributes, bool bInheritHandles,
      uint dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory,
      [In] ref STARTUPINFO lpStartupInfo,
      out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    private static int Main(string[] args)
    {
      var exe = Assembly.GetExecutingAssembly().Location;
      var dir = Path.GetDirectoryName(exe);
      var name = Path.GetFileNameWithoutExtension(exe);

      var configPath = Path.Combine(dir, name + ".shim");
      if (!File.Exists(configPath))
      {
        Console.Error.WriteLine("Couldn't find " + Path.GetFileName(configPath) + " in " + dir);
        return 1;
      }

      var config = Config(configPath);
      var path = Get(config, "path");
      var addArgs = Get(config, "args");

      var si = new STARTUPINFO();
      var pi = new PROCESS_INFORMATION();

      // create command line
      var cmdArgs = addArgs ?? "";
      var passArgs = GetArgs(Environment.CommandLine);
      if (!string.IsNullOrEmpty(passArgs))
      {
        if (!string.IsNullOrEmpty(cmdArgs))
        {
          cmdArgs += " ";
        }

        cmdArgs += passArgs;
      }

      if (!string.IsNullOrEmpty(cmdArgs))
      {
        cmdArgs = " " + cmdArgs;
      }

      var cmd = "\"" + path + "\"" + cmdArgs;

      Debug.WriteLine(cmd);

      SetEnv(config);

      if (!CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero,
        true,
        0,
        IntPtr.Zero, // inherit parent
        null, // inherit parent
        ref si,
        out pi))
      {
        var error = Marshal.GetLastWin32Error();
        if (error == ERROR_ELEVATION_REQUIRED)
        {
          // Unfortunately, ShellExecute() does not allow us to run program without
          // CREATE_NEW_CONSOLE, so we can not replace CreateProcess() completely.
          // The good news is we are okay with CREATE_NEW_CONSOLE when we run program with elevation.
          var process = new Process();
          process.StartInfo = new ProcessStartInfo(path, cmdArgs);
          process.StartInfo.UseShellExecute = true;
          try
          {
            process.Start();
          }
          catch (Win32Exception exception)
          {
            return exception.ErrorCode;
          }

          process.WaitForExit();
          return process.ExitCode;
        }

        return error;
      }

      WaitForSingleObject(pi.hProcess, INFINITE);

      uint exit_code = 0;
      GetExitCodeProcess(pi.hProcess, out exit_code);

      // Close process and thread handles.
      CloseHandle(pi.hProcess);
      CloseHandle(pi.hThread);

      return (int) exit_code;
    }

    // now uses GetArgs instead
    private static string Serialize(string[] args)
    {
      return string.Join(" ", args.Select(a => a.Contains(' ') ? '"' + a + '"' : a));
    }

    // strips the program name from the command line, returns just the arguments
    private static string GetArgs(string cmdLine)
    {
      if (cmdLine.StartsWith("\""))
      {
        var endQuote = cmdLine.IndexOf("\" ", 1);
        if (endQuote < 0)
        {
          return "";
        }

        return cmdLine.Substring(endQuote + 1);
      }

      var space = cmdLine.IndexOf(' ');
      if (space < 0 || space == cmdLine.Length - 1)
      {
        return "";
      }

      return cmdLine.Substring(space + 1);
    }

    private static string Get(Dictionary<string, string> dic, string key)
    {
      string value = null;
      dic.TryGetValue(key, out value);
      return value;
    }

    private static Dictionary<string, string> Config(string path)
    {
      var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      foreach (var line in File.ReadAllLines(path))
      {
        var m = Regex.Match(line, @"([^=]+)=(.*)");
        if (m.Success)
        {
          config[m.Groups[1].Value.Trim()] = m.Groups[2].Value.Trim();
        }
      }

      return config;
    }

    private static void SetEnv(Dictionary<string, string> config)
    {
      foreach (var configKey in config)
      {
        if (configKey.Key.StartsWith("env:"))
        {
          var name = configKey.Key.Substring("env:".Length);
          Environment.SetEnvironmentVariable(name, configKey.Value);
        }

        if (configKey.Key.StartsWith("envclear:"))
        {
          var name = configKey.Key.Substring("envclear:".Length);
          Environment.SetEnvironmentVariable(name, null);
        }

        if (configKey.Key.StartsWith("envappend:"))
        {
          var name = configKey.Key.Substring("envappend:".Length);

          var currentValue = Environment.GetEnvironmentVariable(name);

          string separator;
          if (!config.TryGetValue("envsep:" + name, out separator))
          {
            separator = Path.PathSeparator.ToString();
          }

          string newValue;

          if (string.IsNullOrEmpty(currentValue))
          {
            newValue = configKey.Value;
          }
          else
          {
            newValue = currentValue + separator + configKey.Value;
          }

          Environment.SetEnvironmentVariable(name, newValue);
        }

        if (configKey.Key.StartsWith("envprepend:"))
        {
          var name = configKey.Key.Substring("envprepend:".Length);

          var currentValue = Environment.GetEnvironmentVariable(name);

          string separator;
          if (!config.TryGetValue("envsep:" + name, out separator))
          {
            separator = Path.PathSeparator.ToString();
          }

          string newValue;
          if (string.IsNullOrEmpty(currentValue))
          {
            newValue = configKey.Value;
          }
          else
          {
            newValue = configKey.Value + separator + currentValue;
          }

          Environment.SetEnvironmentVariable(name, newValue);
        }
      }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
      public readonly int cb;
      public readonly string lpReserved;
      public readonly string lpDesktop;
      public readonly string lpTitle;
      public readonly int dwX;
      public readonly int dwY;
      public readonly int dwXSize;
      public readonly int dwYSize;
      public readonly int dwXCountChars;
      public readonly int dwYCountChars;
      public readonly int dwFillAttribute;
      public readonly int dwFlags;
      public readonly short wShowWindow;
      public readonly short cbReserved2;
      public readonly IntPtr lpReserved2;
      public readonly IntPtr hStdInput;
      public readonly IntPtr hStdOutput;
      public readonly IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_INFORMATION
    {
      public IntPtr hProcess;
      public IntPtr hThread;
      public int dwProcessId;
      public int dwThreadId;
    }
  }
}
