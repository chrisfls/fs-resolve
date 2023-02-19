# fs-resolve

Unlike other functional languages, F# compilation process relies on manual file ordering.

With this tool, the ordering is automated by generating a `.targets` file containing the files in the correct order.

## How it Works

The tool scans your F# project for any `// @after ./file` comments and builds a dependency graph based on the relationships between files. It then flattens the graph to generate a linear order of files for compilation.

Any errors encountered during the process will be reported back to the developer for manual resolution, but even with errors present, the tool will still generate a mostly valid resolution for file ordering.

## How to Use

To use the tool, follow these simple steps:

1. Clone or download the tool from the GitHub repository.
2. Navigate to the tool's directory.
3. Run the `build.sh` script to build the tool.
4. Once built, you can generate a targets file for your F# project by running the tool with the following command: `resolve ./path/to/project.fsproj`. The tool will automatically scan your project for `// @after` comments and generate a targets file with the correct file order for compilation.
5. You can also run the tool on multiple `.fsproj` files at once by specifying their paths separated by spaces.

Note that the tool has no dependencies other than the `dotnet-sdk`, so there is no need to install any additional package.

## Contributions

Contributions are welcome! If you have any ideas for improvements or new features, please feel free to submit a pull request.

Just note that this tool is already complete and fully functional.

In the past, I created a version of this tool using `deno` that included some additional features like a watch mode and incremental builds. But that version was already so fast (I benchmarked it with over 10k files) that I ended up never using these features.

This tool should be even faster due the better multithreading model of F# (such as not having to deal with web workers).

While I'm open to accepting contributions, I don't believe that there is a need for further improvements.

Except for the included CLI interface. It works for me, but I acknowledge that it could use some refinement....

As someone who came from Elm, I'm not sure if my code is idiomatic F# or not.
