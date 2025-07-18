# YSRun-Single -- a single-turn GlkOte wrapper for the YarnSpinner interpreter

This is a C# ([.NET][dotnet]) program which starts a YarnSpinner game, executes one turn, and exits. The input and output are JSON stanzas in the [GlkOte][] format.

To run a Yarn game, it must be in the "machine-readable" JSON format produced by `ysc --stdout`. See [YarnSpinner-Console][ysconsole].

The program relies on a modified version of the C# [YarnSpinner][] library. (The modifications have to do with serializing the library state in mid-dialogue.) At present this exists as a [fork][ysfork] on Github.

This script is meant to be used with the [Discoggin][] Discord bot.

[YarnSpinner]: https://github.com/YarnSpinnerTool/YarnSpinner
[ysconsole]: https://github.com/YarnSpinnerTool/YarnSpinner-Console
[ysfork]: https://github.com/erkyrath/YarnSpinner/tree/autosave
[Discoggin]: https://github.com/iftechfoundation/discoggin
[GlkOte]: https://eblong.com/zarf/glk/glkote/docs.html
[GlkOteInit]: https://eblong.com/zarf/glk/glkote/docs.html#input
[dotnet]: https://dotnet.microsoft.com/en-us/download

## To Build

Install the current version of [.NET][dotnet] if necessary. (That's 9.0 at the time of this writing.)

You also have to install the [.NET 6.0 runtime][net6] for some reason known only to Microsoft.

[net6]: https://aka.ms/dotnet-core-applaunch?framework=Microsoft.NETCore.App&framework_version=6.0.0

Check out the [YarnSpinner][ysfork] fork in directory `YarnSpinner` and switch to the `autosave` branch:

```
git clone git@github.com:erkyrath/YarnSpinner.git
cd YarnSpinner
git checkout autosave
cd ..
```

(The top level of the respository should now have both `YSRun-Single` and `YarnSpinner` directories. Sorry, it's a confusing arrangement.)

Now you can build the .NET application:

```
cd YSRun-Single
dotnet build
```

## Usage

```
ysrun [ --start ] [ --autodir DIR ] GAME.yarn.json
```

If `--start` is used, we start the YS game, wait for the [`init`][GlkOteInit] event, and display the game's initial text. 

If `--start` is *not* used, we attempt to load the YS game state from a file called `autosave.json`. (Use `--autodir` to determine what directory this file is found in.) Then we wait for a [`hyperlink`][GlkOteInit] event, select the choice, and display the game's response.

Either way, we write out `autosave.json` in preparation for the next turn.

## Credits

The YSRunSingle.cs wrapper was written by Andrew Plotkin, and is in the public domain.

The [YarnSpinner][] interpreter was created by Yarn Spinner Pty. Ltd., Secret Lab Pty. Ltd., and Yarn Spinner contributors; it is distributed under the MIT license.
