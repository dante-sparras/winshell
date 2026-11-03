namespace Winshell.Commands;

internal static class HelpCommand
{
    internal static int Run(string? command = null)
    {
        Print(command);
        return 0;
    }

    internal static void Print(string? command)
    {
        switch (command)
        {
            case "list":
                WriteHelp("""
                    winshell list [-j]
                      Lists running instances. -j, --json prints them as JSON.
                      Stopped instances are not listed.
                    """);
                return;
            case "kill":
                WriteHelp("""
                    winshell kill [--force] [-i <id> | --pid <pid> | -c <name> | -p <path>]
                      Asks one running instance to exit, then waits until that process has exited.
                      --force ends the process when it does not exit.
                      With one running instance and no selector, that instance is used.
                      With several, this exits and lists them.
                    """);
                return;
            case "reload":
                WriteHelp("""
                    winshell reload [--hard] [-i <id> | --pid <pid> | -c <name> | -p <path>]
                      Builds a configuration project and asks its instance to start again.
                      A build that fails leaves the instance running and asks it to show the error.
                      The default shell starts again. --hard is accepted.
                      With one running instance and no selector, that instance is used.
                      With several, this exits and lists them.
                    """);
                return;
            case "inspect":
                WriteHelp("""
                    winshell inspect [instance options] <command>
                      tree                            print each open window's live layout as JSON
                      screenshot <file> [window]      save a window as a PNG
                      click <name> [window]           activate an element, as Narrator does
                      hover <name> [window]           put a control in its pointer look
                      A window is the type of its content, or its number in tree. The first is the default.
                      A name is an x:Name, or an automation id or name. Nothing goes through Windows' input.
                    """);
                return;
            case "dev":
                WriteHelp("""
                    winshell dev [-c <name> | -p <path>]
                      Runs a configuration project and applies each edit to the running shell, until Ctrl+C.
                      A saved C# or XAML file reaches the shell in place, and the other windows are left alone.
                      What does not compile shows on screen, and the last version that did keeps running.
                      Needs the .NET SDK. See the guide for what an edit can change.
                    """);
                return;
            case "log":
                WriteHelp("""
                    winshell log [-i <id> | --pid <pid> | -c <name> | -p <path>] [-t <n>] [-f] [file]
                      Prints an instance's log. -t, --tail <n> prints the last n lines.
                      -f, --follow keeps printing new lines. winshell log <file> prints that file.
                      With one running instance and no selector, that instance is used.
                      With several, this exits and lists them.
                      With nothing running and no selector, the log of a stopped instance is printed.
                    """);
                return;
            case "ipc":
                WriteHelp("""
                    winshell ipc [-i <id> | --pid <pid> | -c <name> | -p <path>] <command>
                      show [-j]                             list IPC targets. -j prints JSON
                      call <target> <function> [args...]    call a function and print the result
                      call notice dismiss                   close Winshell's own notice
                      prop get <target> <property>          print a property
                      wait <target> <signal>                wait for one signal
                      listen <target> <signal>              print each signal until Ctrl+C
                      Words after the function name are arguments, even when they start with -.
                      A -- ends options on every command.
                    """);
                return;
            case "doctor":
                WriteHelp($"""
                    winshell doctor [-c <name> | -p <path>] [-j] [--no-color]
                      Checks this PC's setup for Winshell and changes nothing.
                      -c, --config <name>    a named config in {ConfigPaths.Root()}
                      -j, --json             print the checks as JSON
                          --no-color         print without color
                    """);
                return;
            case "msg":
                WriteHelp("""
                    winshell msg is the old name of winshell ipc call.
                    """);
                return;
        }
        WriteHelp($"""
            winshell {CliMetadata.Version}: a desktop shell for Windows 11

            Usage: winshell run [options]              start a shell
                   winshell                            print this help
                   winshell <command> --help           help for that command, which does not run
                   winshell list [-j]                  list running instances
                   winshell kill [--force] [instance options]
                                                       stop an instance and wait until it exits
                   winshell reload [--hard] [instance options]
                                                       reload an instance's configuration
                   winshell dev [-c <name> | -p <path>]
                                                       run a project and apply each edit to it
                   winshell log [-f] [-t N] [file]     print an instance's log
                   winshell ipc show [-j]              list IPC targets
                   winshell ipc call <target> <function> [args...]
                   winshell ipc prop get <target> <property>
                   winshell ipc wait <target> <signal>
                   winshell ipc listen <target> <signal>
                   winshell inspect tree | screenshot <file> | click <name> | hover <name>
                                                       look at a running shell, or act on it
                   winshell doctor [config options] [-j] [--no-color]
                                                       check this PC's setup for Winshell
                   winshell help                       print this summary

            Config selection:
              -p, --path <path>      a project file, or the folder it is in
              -c, --config <name>    a named config in {ConfigPaths.Root()}
                                     ({ConfigPaths.OverrideVariable} overrides that folder)
            Instance selection (kill, reload, log, ipc):
              -i, --id <id>          an instance id, or the start of one
                  --pid <pid>        an instance's process id
            Other:
              -d, --daemonize        run in the background
              -n, --no-duplicate     exit quietly if this config is already running
                  --no-build         run the last build, and do not build again
              -v, --verbose          log debug messages
              -j, --json             print list, ipc show or doctor as JSON
                  --no-color         print doctor without color
              -h, --help, /?         print help and exit
              -V, --version          print the version
              --                     end options; later words are the program's arguments

            Exit codes: 0 done, 1 the command failed, 2 the command line was wrong.
            """);
    }

    private static void WriteHelp(string text) => Console.WriteLine(text.Replace("\r\n", "\n").TrimEnd());
}
