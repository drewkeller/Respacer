# Respacer

Apply indentation to a solution, a project, a folder, or a file. 
This is a great way to normalize the use of tabs or spaces without affecting other formatting. 

Tabify and Untabify commands are provided on the right click menus in the Solution Explorer or 
in the code editor. Indent size and tab size are applied according to the Visual Studio editor 
options for the given file.

## Hints and Tips

 * Indent size refers to the amount of space at the beginning of indented lines, per tab.
 * Tab size is the amount of spacing after non-whitespace.
 * When invoked on a solution. project, or folder, only the following file types are included in the 
process: `.cpp, .c, .h, .hpp, .cs, .js, .vb, .txt, .scss, .coffee, .ts, .jsx, .markdown, 
.md, .config, .css`.
 * When operating on manually selected files or a file open in the code editor, the file types are 
 not checked, so it works on any file.
 * It may be helpful have a visualization of the whitespace in Visual Studio. It can be turned on 
using the menu option `Edit > Advanced > View White Space` or key combination `Ctrl-E, S`.

## License

[The MIT License](LICENSE.md)