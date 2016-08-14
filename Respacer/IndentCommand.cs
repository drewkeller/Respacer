//------------------------------------------------------------------------------
// <copyright file="UntabifyCommand.cs" company="Drew Keller">
//     Copyright (c) Drew Keller.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.ComponentModel.Design;
using System.Globalization;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using EnvDTE;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DrewKeller.Respacer
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class UntabifyCommand
    {
        private const string _supportedExtensions = ".cpp, .c, .h, .hpp, .cs, .js, .vb, .txt, .scss, .coffee, .ts, .jsx, .markdown, .md, .config, .css";
        public string[] SupportedExtensions = _supportedExtensions.Split(',');

        private enum Operation
        {
            Tabify, UnTabify
        }

        #region Helper classes

        /// <summary>
        /// Encapsulates settings to pass between methods.
        /// </summary>
        private class Settings
        {
            public Operation Operation { get; set; }
            public int IndentSize { get; set; }
            public int TabSize { get; set; }
            public bool ApplyToIndentOnly { get; set; }
            public Settings(Operation op, int indentSize, int tabSize, bool applyToIndentOnly)
            {
                Operation = op; IndentSize = indentSize; TabSize = TabSize; ApplyToIndentOnly = applyToIndentOnly;
            }
        }

        /// <summary>
        /// Keep track of counts.
        /// </summary>
        private class Tally
        {
            public int Indents { get; set; }
            public int Files { get; set; }
            public int Projects { get; set; }

            public void AddProjectTally (Tally tally)
            {
                if (tally.Indents > 0) {
                    Indents += tally.Indents;
                    Files += tally.Files;
                    Projects++;
                }
            }

            public void AddFileTally (Tally tally)
            {
                if (tally.Indents > 0) {
                    Indents += tally.Indents;
                    Files++;
                }
            }
        }

        #endregion // Helper classes

        #region Command and menu IDs

        /// <summary>
        /// Command ID.
        /// </summary>
        /// <remark>Match these to something in the Symbol section of the vsct.</remark>
        public const int CMD_UnTabifyText = 0x100;
        public const int CMD_UnTabifyFile = 0x101;
        public const int CMD_UnTabifyFolder = 0x102;
        public const int CMD_UnTabifyProject = 0x103;
        public const int CMD_UnTabifySolution = 0x104;
        public const int CMD_TabifyText = 0x200;
        public const int CMD_TabifyFile = 0x201;
        public const int CMD_TabifyFolder = 0x202;
        public const int CMD_TabifyProject = 0x203;
        public const int CMD_TabifySolution = 0x204;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid guidUntabifyText = new Guid("9a046398-6803-44b3-b897-7327ee81e1e0");
        public static readonly Guid guidUntabifyFile = new Guid("cbf0b286-9e0a-45dd-92f6-5dcfc971b277");
        public static readonly Guid guidUntabifyFolder = new Guid("57e87176-3945-4403-a310-d9a78567a9d6");
        public static readonly Guid guidUntabifyProject = new Guid("e2db1dd0-d7ff-46fd-8982-142bf3fb233c");
        public static readonly Guid guidUntabifySolution = new Guid("e1022069-4113-4e61-8f62-ff3c988ca1f2");
        public static readonly Guid guidTabifyText = new Guid("325ab79d-926c-44bd-92d1-dd3e6670775c");
        public static readonly Guid guidTabifyFile = new Guid("fb7c5b72-6f99-43b8-a173-787fc31f1edf");
        public static readonly Guid guidTabifyFolder = new Guid("0d5a306a-6817-4db6-8f83-cf591fdd7830");
        public static readonly Guid guidTabifyProject = new Guid("628e66a7-634d-42ca-8d11-449b65009e56");
        public static readonly Guid guidTabifySolution = new Guid("b52a3c14-b757-415c-b442-699570732ff1");

        #endregion // Command and menu IDs

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly Package package;

        #region Properties

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private IServiceProvider ServiceProvider {
            get {
                return this.package;
            }
        }

        /// <summary>
        /// The top level object of the Visual Studio IDE.
        /// </summary>
        public DTE IDE {
            get { return _ide ?? (_ide = (DTE)this.ServiceProvider.GetService(typeof(DTE))); }
        }
        private DTE _ide;

        #endregion

        #region Constructors, initialization

        /// <summary>
        /// Initializes a new instance of the <see cref="UntabifyCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        private UntabifyCommand(Package package)
        {
            if (package == null) {
                throw new ArgumentNullException("package");
            }

            this.package = package;

            OleMenuCommandService srv = this.ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (srv != null) {
                AddMenuCommand(srv, guidTabifyText, CMD_TabifyText, TabifyTextEventHandler);
                AddMenuCommand(srv, guidTabifyFile, CMD_TabifyFile, TabifyFileEventHandler);
                AddMenuCommand(srv, guidTabifyFolder, CMD_TabifyFolder, TabifyFolderEventHandler);
                AddMenuCommand(srv, guidTabifyProject, CMD_TabifyProject, TabifyProjectEventHandler);
                AddMenuCommand(srv, guidTabifySolution, CMD_TabifySolution, TabifySolutionEventHandler);
                AddMenuCommand(srv, guidUntabifyText, CMD_UnTabifyText, UntabifyTextEventHandler);
                AddMenuCommand(srv, guidUntabifyFile, CMD_UnTabifyFile, UntabifyFileEventHandler);
                AddMenuCommand(srv, guidUntabifyFolder, CMD_UnTabifyFolder, UntabifyFolderEventHandler);
                AddMenuCommand(srv, guidUntabifyProject, CMD_UnTabifyProject, UntabifyProjectEventHandler);
                AddMenuCommand(srv, guidUntabifySolution, CMD_UnTabifySolution, UntabifySolutionEventHandler);
            }
        }

        private void AddMenuCommand(OleMenuCommandService mcs, Guid menuGroup, int commandID, EventHandler handler)
        {
            var cmd = new CommandID(menuGroup, commandID);
            var itm = new MenuCommand(handler, cmd);
            itm.Visible = true;
            itm.Enabled = true;
            mcs.AddCommand(itm);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static UntabifyCommand Instance {
            get;
            private set;
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static void Initialize(Package package)
        {
            Instance = new UntabifyCommand(package);
        }

        #endregion // Constructors, initialization

        #region Event handlers

        private void TabifyTextEventHandler(object sender, EventArgs e)
        {
            ApplyToSelectedItems(CMD_TabifyText);
        }

        private void TabifyFileEventHandler(object sender, EventArgs e)
        {
            ApplyToSelectedItems(CMD_TabifyFile);
        }

        private void TabifyFolderEventHandler(object sender, EventArgs e)
        {
            ApplyToSelectedItems(CMD_TabifyFolder);
        }

        private void TabifyProjectEventHandler(object sender, EventArgs e)
        {
            ApplyToSelectedItems(CMD_TabifyProject);
        }

        private void TabifySolutionEventHandler(object sender, EventArgs e)
        {
            ApplyToSelectedItems(CMD_TabifySolution);
        }

        private void UntabifyTextEventHandler(object sender, EventArgs e)
        {
            ApplyToSelectedItems(CMD_UnTabifyText);
        }

        private void UntabifyFileEventHandler(object sender, EventArgs e)
        {
            ApplyToSelectedItems(CMD_UnTabifyFile);
        }

        private void UntabifyFolderEventHandler(object sender, EventArgs e)
        {
            ApplyToSelectedItems(CMD_UnTabifyFolder);
        }

        private void UntabifyProjectEventHandler(object sender, EventArgs e)
        {
            ApplyToSelectedItems(CMD_UnTabifyProject);
        }

        private void UntabifySolutionEventHandler(object sender, EventArgs e)
        {
            ApplyToSelectedItems(CMD_UnTabifySolution);
        }

        #endregion // Event handlers

        #region Command logic

        /// <summary>
        /// Provide a common place to handle the specific command (e.g. file vs project)
        /// </summary>
        /// <param name="command"></param>
        private void ApplyToSelectedItems(int command)
        {
            var tally = new Tally();
            var newTally = new Tally();
            var op = Operation.UnTabify;
            System.Threading.Tasks.Task.Run(() => {
                    switch (command) {

                        case CMD_TabifyText:
                        case CMD_UnTabifyText:
                            op = command == CMD_TabifyText ? Operation.Tabify : Operation.UnTabify;
                            ApplyToSelectedText(op, newTally);
                            tally.AddFileTally(newTally);
                            break;

                        case CMD_TabifyFile:
                        case CMD_UnTabifyFile:
                            foreach (SelectedItem selectedItem in IDE.SelectedItems) {
                                op = command == CMD_TabifyFile ? Operation.Tabify : Operation.UnTabify;
                                ApplyToProjectItem(op, newTally, selectedItem.ProjectItem);
                                tally.AddFileTally(newTally);
                            }
                            break;

                        case CMD_TabifyFolder:
                        case CMD_UnTabifyFolder:
                            foreach (SelectedItem selectedItem in IDE.SelectedItems) {
                                op = command == CMD_TabifyFolder ? Operation.Tabify : Operation.UnTabify;
                                ApplyToProjectItems(op, newTally, selectedItem.ProjectItem.ProjectItems);
                                tally.AddFileTally(newTally);
                            }
                            break;

                        case CMD_TabifyProject:
                        case CMD_UnTabifyProject:
                            foreach (SelectedItem selectedItem in IDE.SelectedItems) {
                                op = command == CMD_TabifyProject ? Operation.Tabify : Operation.UnTabify;
                                ApplyToProjectItems(op, newTally, selectedItem.Project.ProjectItems);
                                tally.AddFileTally(newTally);
                            }
                            break;

                        case CMD_TabifySolution:
                        case CMD_UnTabifySolution:
                            op = command == CMD_TabifySolution ? Operation.Tabify : Operation.UnTabify;
                            var solution = this.IDE.Solution;
                            foreach (Project project in solution.Projects) {
                                if (project == null) continue;
                                newTally = new Tally();
                                ApplyToProjectItems(op, tally, project.ProjectItems);
                                tally.AddProjectTally(newTally);
                            }
                            break;
                    }

                string indentStr = Pluralize(tally.Indents, "indent", "indents");
                string fileStr = Pluralize(tally.Files, "file", "files");
                VsShellUtilities.ShowMessageBox(this.ServiceProvider,
                    string.Format("Changed {0} in {1}.", indentStr, fileStr), "Indents Applied",
                    OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
            );
        }

        private string Pluralize(int number, string singular, string plural)
        {
            return string.Format("{0} {1}", number, number == 1 ? singular : plural);
        }

        /// <summary>
        /// Recursively operate on each project item and its children.
        /// </summary>
        /// <param name="projectItems"></param>
        /// <returns></returns>
        private void ApplyToProjectItems(Operation op, Tally tally, ProjectItems projectItems)
        {
            foreach (ProjectItem item in projectItems) {
                if (item.ProjectItems != null && item.ProjectItems.Count > 0) {
                    ApplyToProjectItems(op, tally, item.ProjectItems);
                } else foreach (string ext in SupportedExtensions) {
                    if (item.Name.ToLower().EndsWith(ext.ToLower().Trim())) {
                        var newTally = new Tally();
                        ApplyToProjectItem(op, newTally, item);
                        if (newTally.Files > 0) {
                            tally.Files += newTally.Files;
                            tally.Indents += newTally.Indents;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Operate on a single project item.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        private void ApplyToProjectItem(Operation op, Tally tally, ProjectItem item)
        {
            Window docWindow = null;
            bool isOpen = true;
            if (!item.IsOpen) {
                docWindow = item.Open();
                isOpen = false;
            }

            var doc = item.Document;
            if (doc == null) return;
            var textDoc = doc.Object("TextDocument") as TextDocument;
            if (textDoc == null) return;

            var start = textDoc.StartPoint.CreateEditPoint();
            var end = textDoc.EndPoint.CreateEditPoint();
            var text = start.GetText(end.AbsoluteCharOffset);
            var newTally = new Tally();
            ApplyToTextDoc(op, newTally, textDoc, start, text);
            if (newTally.Files > 0) {
                tally.Files += newTally.Files;
                tally.Indents += newTally.Indents;
                if (!doc.Saved) doc.Save();
            }
            if (!isOpen) docWindow.Close();
        }

        /// <summary>
        /// Operate on the selected text in the active code window (text document).
        /// </summary>
        /// <returns></returns>
        private void ApplyToSelectedText(Operation op, Tally tally)
        {
            Document doc = IDE.ActiveDocument;
            TextDocument textDoc = doc.Object() as TextDocument;
            if (textDoc == null) return;
            var selection = textDoc.Selection;
            string text = selection.Text;
            var start = selection.TopPoint.CreateEditPoint();
            // the BottomPoint doesn't always work as expected, such as when a full line is selected
            //var end = selection.BottomPoint.CreateEditPoint();
            // if nothing is selected, operate on the whole document
            if (text == null || text == string.Empty) {
                start = textDoc.StartPoint.CreateEditPoint();
                var end = textDoc.EndPoint.CreateEditPoint();
                text = start.GetText(end.AbsoluteCharOffset);
            }
            ApplyToTextDoc(op, tally, textDoc, start, text);
        }

        /// <summary>
        /// Operate on text within a text document.
        /// </summary>
        private void ApplyToTextDoc(Operation op, Tally tally, TextDocument textDoc, EditPoint start, string text)
        {
            var indentSize = textDoc.IndentSize;
            var tabSize = textDoc.TabSize;
            //var text = start.GetText(end.AbsoluteCharOffset);
            //text = start.GetLines(start.Line, end.Line);
            //int startOffset = start.AbsoluteCharOffset;
            //int endOffset = end.AbsoluteCharOffset;
            var settings = new Settings(op, indentSize, tabSize, false);
            var newTally = new Tally();
            var newText = ApplyToText(settings, newTally, text);
            if (newTally.Indents > 0) {
                tally.Files++;
                tally.Indents += newTally.Indents;
                start.ReplaceText(text.Length, newText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
            }
        }

        /// <summary>
        /// Operate on a piece of text, usually some or all of the text from a file.
        /// </summary>
        private string ApplyToText(Settings settings, Tally tally, string text)
        {
            Regex regex = null;
            int indentSize = settings.IndentSize;
            int tabSize = settings.TabSize;

            // similar to http://stackoverflow.com/questions/4994225/count-regex-replaces-c
            // and http://stackoverflow.com/questions/11713212/finding-tabs-or-4-spaces-in-c-sharp-code

            // replace indenting tabs at beginning of the line...
            int numIndents = 0;
            if (indentSize >= 0) {
                if (settings.Operation == Operation.UnTabify) {
                    regex = new Regex(@"^\t+", RegexOptions.Multiline);
                    text = regex.Replace(text, (m) => {
                        numIndents++;
                        string replacement = new string(' ', indentSize * m.Value.Length);
                        return m.Result(replacement);
                    });
                } else {
                    regex = new Regex("^(" + new string(' ', indentSize) + ")+", RegexOptions.Multiline);
                    text = regex.Replace(text, (m) => {
                        numIndents++;
                        string replacement = new string('\t', m.Value.Length / indentSize);
                        return m.Result(replacement);
                    });
                }
                tally.Indents += numIndents;
            }

            // replace non-indenting tabs on a line...
            if (!settings.ApplyToIndentOnly && tabSize > 0) {
                numIndents = 0;
                if (settings.Operation == Operation.UnTabify) {
                    regex = new Regex(@"\t+");
                    text = regex.Replace(text, (m) => {
                        numIndents++;
                        string replacement = new string(' ', tabSize * m.Value.Length);
                        return m.Result(replacement);
                    });
                } else {
                    regex = new Regex("(" + new string(' ', tabSize) + ")+");
                    text = regex.Replace(text, (m) => {
                        numIndents++;
                        string replacement = new string('\t', m.Value.Length / tabSize);
                        return m.Result(replacement);
                    });
                }
                tally.Indents += numIndents;
            }
            return text;
        }

        #endregion // Command logic

        #region Project navigation helpers

        private List<Project> GetProjects(Solution solution)
        {
            var projects = new List<Project>();
            foreach (Project project in solution.Projects) {
                if (project == null) continue;
                if (project.Kind == EnvDTE80.ProjectKinds.vsProjectKindSolutionFolder) {
                    projects.AddRange(GetProjects(project));
                } else {
                    projects.Add(project);
                }
            }
            return projects;
        }

        private List<Project> GetProjects(Project solutionFolder)
        {
            var projects = new List<Project>();
            foreach (Project project in solutionFolder.ProjectItems) {
                if (project == null) continue;
                if (project.Kind == EnvDTE80.ProjectKinds.vsProjectKindSolutionFolder) {
                    projects.AddRange(GetProjects(project));
                } else {
                    projects.Add(project);
                }
            }
            return projects;
        }

        #endregion // Project navigation helpers

    }
}
