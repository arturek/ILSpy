// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

using AvalonDock;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.FlowAnalysis;
using ICSharpCode.ILSpy.TextView;
using ICSharpCode.ILSpy.TreeNodes;
using ICSharpCode.ILSpy.TreeNodes.Analyzer;
using ICSharpCode.TreeView;
using Microsoft.Win32;
using Mono.Cecil;

namespace ICSharpCode.ILSpy
{
	/// <summary>
	/// The main window of the application.
	/// </summary>
	[Export(typeof(IDocumentsManager))]
	partial class MainWindow : Window, IDocumentsManager
	{
		ILSpySettings spySettings;
		SessionSettings sessionSettings;
		AssemblyListManager assemblyListManager;
		AssemblyList assemblyList;
		AssemblyListTreeNode assemblyListTreeNode;
		TypesTreeView typesTreeView;
		bool treeViewInitialized;
		DecompilerDocument lastActiveDocument;
				
		static MainWindow instance;
		
		public static MainWindow Instance {
			get { return instance; }
		}
		
		public MainWindow()
		{
			instance = this;
			spySettings = ILSpySettings.Load();
			this.sessionSettings = new SessionSettings(spySettings);
			this.assemblyListManager = new AssemblyListManager(spySettings);
			
			if (Environment.OSVersion.Version.Major >= 6)
				this.Icon = new BitmapImage(new Uri("pack://application:,,,/ILSpy;component/images/ILSpy.ico"));
			else
				this.Icon = Images.AssemblyLoading;
			
			this.DataContext = sessionSettings;
			this.Left = sessionSettings.WindowBounds.Left;
			this.Top = sessionSettings.WindowBounds.Top;
			this.Width = sessionSettings.WindowBounds.Width;
			this.Height = sessionSettings.WindowBounds.Height;
			// TODO: validate bounds (maybe a monitor was removed...)
			this.WindowState = sessionSettings.WindowState;
			
			InitializeComponent();
			App.CompositionContainer.ComposeParts(this);
			
			this.typesTreeView = new MainWindow.TypesTreeView(this.treeView);
			this.typesTreeView.SelectionChanged += delegate { DecompileSelectedNodes(null, newWindow: false, recordInHistory: false); };
			
			sessionSettings.FilterSettings.PropertyChanged += filterSettings_PropertyChanged;
			
			InitMainMenu();
			InitToolbar();
			
			
			this.LoadDocuments(sessionSettings.GetSettings("{uri://sharpdevelop.net/ilspy}OpenDocuments"));
			
			this.Loaded += new RoutedEventHandler(MainWindow_Loaded);

			this.dockManager.Loaded += delegate {
				if(sessionSettings.DockManagerSettings != null)
				{
					dockManager.RestoreLayout(sessionSettings.DockManagerSettings.CreateReader());
					SheduleNextAction(ActivateVisibleDocuments);
				}
			 };
		}
		
		#region Toolbar extensibility
		[ImportMany("ToolbarCommand", typeof(ICommand))]
		Lazy<ICommand, IToolbarCommandMetadata>[] toolbarCommands = null;
		
		void InitToolbar()
		{
			int navigationPos = 0;
			int openPos = 1;
			foreach (var commandGroup in toolbarCommands.OrderBy(c => c.Metadata.ToolbarOrder).GroupBy(c => c.Metadata.ToolbarCategory)) {
				if (commandGroup.Key == "Navigation") {
					foreach (var command in commandGroup) {
						toolBar.Items.Insert(navigationPos++, MakeToolbarItem(command));
						openPos++;
					}
				} else if (commandGroup.Key == "Open") {
					foreach (var command in commandGroup) {
						toolBar.Items.Insert(openPos++, MakeToolbarItem(command));
					}
				} else {
					toolBar.Items.Add(new Separator());
					foreach (var command in commandGroup) {
						toolBar.Items.Add(MakeToolbarItem(command));
					}
				}
			}
			
		}
		
		Button MakeToolbarItem(Lazy<ICommand, IToolbarCommandMetadata> command)
		{
			return new Button {
				Command = CommandWrapper.Unwrap(command.Value),
				ToolTip = command.Metadata.ToolTip,
				Content = new Image {
					Width = 16,
					Height = 16,
					Source = Images.LoadImage(command.Value, command.Metadata.ToolbarIcon)
				}
			};
		}
		#endregion
		
		#region Main Menu extensibility
		[ImportMany("MainMenuCommand", typeof(ICommand))]
		Lazy<ICommand, IMainMenuCommandMetadata>[] mainMenuCommands = null;
		
		void InitMainMenu()
		{
			foreach (var topLevelMenu in mainMenuCommands.OrderBy(c => c.Metadata.MenuOrder).GroupBy(c => c.Metadata.Menu)) {
				var topLevelMenuItem = mainMenu.Items.OfType<MenuItem>().FirstOrDefault(m => (m.Header as string) == topLevelMenu.Key);
				foreach (var category in topLevelMenu.GroupBy(c => c.Metadata.MenuCategory)) {
					if (topLevelMenuItem == null) {
						topLevelMenuItem = new MenuItem();
						topLevelMenuItem.Header = topLevelMenu.Key;
						mainMenu.Items.Add(topLevelMenuItem);
					} else if (topLevelMenuItem.Items.Count > 0) {
						topLevelMenuItem.Items.Add(new Separator());
					}
					foreach (var entry in category) {
						MenuItem menuItem = new MenuItem();
						menuItem.Command = CommandWrapper.Unwrap(entry.Value);
						if (!string.IsNullOrEmpty(entry.Metadata.Header))
							menuItem.Header = entry.Metadata.Header;
						if (!string.IsNullOrEmpty(entry.Metadata.MenuIcon)) {
							menuItem.Icon = new Image {
								Width = 16,
								Height = 16,
								Source = Images.LoadImage(entry.Value, entry.Metadata.MenuIcon)
							};
						}
						topLevelMenuItem.Items.Add(menuItem);
					}
				}
			}
		}
		#endregion
		
		void MainWindow_Loaded(object sender, RoutedEventArgs e)
		{
			ILSpySettings spySettings = this.spySettings;
			this.spySettings = null;
			
			// Load AssemblyList only in Loaded event so that WPF is initialized before we start the CPU-heavy stuff.
			// This makes the UI come up a bit faster.
			this.assemblyList = assemblyListManager.LoadList(spySettings, sessionSettings.ActiveAssemblyList);
			
			ShowAssemblyList(this.assemblyList);
			
			string[] args = Environment.GetCommandLineArgs();
			for (int i = 1; i < args.Length; i++) {
				assemblyList.OpenAssembly(args[i]);
			}
			if (assemblyList.GetAssemblies().Length == 0)
				LoadInitialAssemblies();

			treeViewInitialized = true;
			ActivateVisibleDocuments();
			
			if(sessionSettings.ActiveTreeViewPath != null)
			{
				SharpTreeNode node = typesTreeView.FindNodeByPath(sessionSettings.ActiveTreeViewPath, true);
				if (node != null) {
					SelectNode(node, newWindow: false);
					
					// only if not showing the about page, perform the update check:
					ShowMessageIfUpdatesAvailableAsync(spySettings);
				} else {
					var doc = OpenNewDocument();
					doc.Title = "About";
					AboutPage.Display(doc.DecompilerTextView);
				}
			} else
				ShowMessageIfUpdatesAvailableAsync(spySettings);
		}
		
		void ActivateVisibleDocuments()
		{
			Debug.WriteLine("Activating visible documents");
			if(!treeViewInitialized)
				return;
			foreach (var doc in ListVisibleDocuments()) {
				ActivateDocument(doc);
			}
		}
		
		void ActivateDocument(DecompilerDocument doc)
		{
			if (doc == null)
				throw new ArgumentNullException("dd");
		
			lastActiveDocument = doc;			
			if(!treeViewInitialized)
				return;
			
			Debug.WriteLine(String.Format("Activating document {0}.", doc.Title));
			if(!doc.Activate(CurrentLanguage)) {
				Debug.WriteLine(String.Format("Document {0} failed to activate and will be closed.", doc.Title));
				// Close document if none of previously selected nodes can be found now.
				SheduleNextAction(() => doc.Document.Close());
			}
		}
		
		void SheduleNextAction(Action action)
		{
			if (action == null)
				throw new ArgumentNullException("action");
			this.Dispatcher.BeginInvoke(DispatcherPriority.Background, action);
		}
		
		#region Update Check
		string updateAvailableDownloadUrl;
		
		void ShowMessageIfUpdatesAvailableAsync(ILSpySettings spySettings)
		{
			AboutPage.CheckForUpdatesIfEnabledAsync(spySettings).ContinueWith(
				delegate (Task<UpdatedDetails> task) {
					if (task.Result != null) {
						
						updateAvailableDownloadUrl = task.Result.DownloadUrl;
						var updateAvailableDocument = new DocumentContent
						{
							Name = "UpdateDoc",
							Title = "New version available",
						};
						updateAvailableDocument.Content = task.Result;
						updateAvailableDocument.Show(dockManager);
					}
				},
				TaskScheduler.FromCurrentSynchronizationContext()
			);
		}
		
		void downloadUpdateButtonClick(object sender, RoutedEventArgs e)
		{
			Process.Start(updateAvailableDownloadUrl);
		}
		#endregion
		
		void ShowAssemblyList(AssemblyList assemblyList)
		{
			this.assemblyList = assemblyList;
			
			assemblyList.assemblies.CollectionChanged += assemblyList_Assemblies_CollectionChanged;
			
			assemblyListTreeNode = new AssemblyListTreeNode(assemblyList);
			assemblyListTreeNode.FilterSettings = sessionSettings.FilterSettings.Clone();
			assemblyListTreeNode.Select = node => SelectNode(node, newWindow: false);
			this.typesTreeView.SetRootNodes(assemblyListTreeNode);
			
			if (assemblyList.ListName == AssemblyListManager.DefaultListName)
				this.Title = "ILSpy";
			else
				this.Title = "ILSpy - " + assemblyList.ListName;
		}

		void assemblyList_Assemblies_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
//			if (e.OldItems != null)
//				foreach (LoadedAssembly asm in e.OldItems)
//					history.RemoveAll(n => n.Item1.Any(nd => nd.AncestorsAndSelf().OfType<AssemblyTreeNode>().Any(a => a.LoadedAssembly == asm)));
		}
		
		void LoadInitialAssemblies()
		{
			// Called when loading an empty assembly list; so that
			// the user can see something initially.
			System.Reflection.Assembly[] initialAssemblies = {
				typeof(object).Assembly,
				typeof(Uri).Assembly,
				typeof(System.Linq.Enumerable).Assembly,
				typeof(System.Xml.XmlDocument).Assembly,
				typeof(System.Windows.Markup.MarkupExtension).Assembly,
				typeof(System.Windows.Rect).Assembly,
				typeof(System.Windows.UIElement).Assembly,
				typeof(System.Windows.FrameworkElement).Assembly,
				typeof(ICSharpCode.TreeView.SharpTreeView).Assembly,
				typeof(Mono.Cecil.AssemblyDefinition).Assembly,
				typeof(ICSharpCode.AvalonEdit.TextEditor).Assembly,
				typeof(ICSharpCode.Decompiler.Ast.AstBuilder).Assembly,
				typeof(MainWindow).Assembly
			};
			foreach (System.Reflection.Assembly asm in initialAssemblies)
				assemblyList.OpenAssembly(asm.Location);
		}

		void filterSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			RefreshTreeViewFilter();
			if (e.PropertyName == "Language") {
				DecompileSelectedNodes(null, newWindow: false, recordInHistory: false);
			}
		}
		
		public void RefreshTreeViewFilter()
		{
			// filterSettings is mutable; but the ILSpyTreeNode filtering assumes that filter settings are immutable.
			// Thus, the main window will use one mutable instance (for data-binding), and assign a new clone to the ILSpyTreeNodes whenever the main
			// mutable instance changes.
			if (assemblyListTreeNode != null)
				assemblyListTreeNode.FilterSettings = sessionSettings.FilterSettings.Clone();
		}
		
		internal AssemblyList AssemblyList {
			get { return assemblyList; }
		}
		
		internal AssemblyListTreeNode AssemblyListTreeNode {
			get { return assemblyListTreeNode; }
		}
		
		#region Node Selection
		internal void SelectNode(SharpTreeNode obj, bool newWindow, bool recordNavigationInHistory = true)
		{
			if (obj != null) {
				DecompileSelectedNodes(new List<SharpTreeNode>{ obj }, newWindow, recordNavigationInHistory);
			}
		}
		
		internal void SelectNode(SharpTreeNode obj, bool newWindow, DecompilerTextView sourceTextView, bool recordNavigationInHistory = true)
		{
			if (obj != null) {
				DecompilerDocument doc = null;
				if(!newWindow)
					doc = sourceTextView != null ? ListDocuments().FirstOrDefault(d => d.DecompilerTextView == sourceTextView) : CurrentOrDefaultDocument;
				
				DecompileSelectedNodes(new List<SharpTreeNode>{ obj }, doc, recordNavigationInHistory);
			}
		}

		public void JumpToReference(object reference, bool newWindow, DecompilerTextView sourceTextView = null)
		{
			if (reference is TypeReference) {
				SelectNode(assemblyListTreeNode.FindTypeNode(((TypeReference)reference).Resolve()), newWindow, sourceTextView);
			} else if (reference is MethodReference) {
				SelectNode(assemblyListTreeNode.FindMethodNode(((MethodReference)reference).Resolve()), newWindow, sourceTextView);
			} else if (reference is FieldReference) {
				SelectNode(assemblyListTreeNode.FindFieldNode(((FieldReference)reference).Resolve()), newWindow, sourceTextView);
			} else if (reference is PropertyReference) {
				SelectNode(assemblyListTreeNode.FindPropertyNode(((PropertyReference)reference).Resolve()), newWindow, sourceTextView);
			} else if (reference is EventReference) {
				SelectNode(assemblyListTreeNode.FindEventNode(((EventReference)reference).Resolve()), newWindow, sourceTextView);
			} else if (reference is AssemblyDefinition) {
				SelectNode(assemblyListTreeNode.FindAssemblyNode((AssemblyDefinition)reference), newWindow, sourceTextView);
			} else if (reference is Mono.Cecil.Cil.OpCode) {
				string link = "http://msdn.microsoft.com/library/system.reflection.emit.opcodes." + ((Mono.Cecil.Cil.OpCode)reference).Code.ToString().ToLowerInvariant() + ".aspx";
				try {
					Process.Start(link);
				} catch {
					
				}
			}
		}
		
		#endregion
		
		#region Open/Refresh
		void OpenCommandExecuted(object sender, ExecutedRoutedEventArgs e)
		{
			e.Handled = true;
			OpenFileDialog dlg = new OpenFileDialog();
			dlg.Filter = ".NET assemblies|*.dll;*.exe|All files|*.*";
			dlg.Multiselect = true;
			dlg.RestoreDirectory = true;
			if (dlg.ShowDialog() == true) {
				OpenFiles(dlg.FileNames);
			}
		}
		
		public void OpenFiles(string[] fileNames)
		{
			if (fileNames == null)
				throw new ArgumentNullException("fileNames");
			treeView.UnselectAll();
			SharpTreeNode lastNode = null;
			foreach (string file in fileNames) {
				var asm = assemblyList.OpenAssembly(file);
				if (asm != null) {
					var node = assemblyListTreeNode.FindAssemblyNode(asm);
					if (node != null) {
						treeView.SelectedItems.Add(node);
						lastNode = node;
					}
				}
			}
			if (lastNode != null)
				treeView.FocusNode(lastNode);
		}
		
		void RefreshCommandExecuted(object sender, ExecutedRoutedEventArgs e)
		{
			e.Handled = true;
			ShowAssemblyList(assemblyListManager.LoadList(ILSpySettings.Load(), assemblyList.ListName));

			foreach (var doc in ListDocuments()) {
				doc.ResetDecompilationState();
			}
			ActivateVisibleDocuments();
		}
		#endregion
		
		#region Decompile
		private void DecompileSelectedNodes(List<SharpTreeNode> nodes, bool newWindow, bool recordInHistory, DecompilerTextViewState state = null)
		{
			var doc = newWindow ? null : CurrentDocument;
			DecompileSelectedNodes(nodes, newWindow ? null : CurrentDocument, recordInHistory, state);
		}

		private void DecompileSelectedNodes(List<SharpTreeNode> nodes, DecompilerDocument doc, bool recordInHistory, DecompilerTextViewState state = null)
		{
			if (doc == null) {
				doc = OpenNewDocument();
				recordInHistory = false;
			}
						
			IEnumerable<SharpTreeNode> sharpTreeNodes = null;
			if(nodes != null) {
				typesTreeView.SilentlySelectNodes(nodes);
				sharpTreeNodes = nodes.OfType<SharpTreeNode>();
			} else {
				sharpTreeNodes = this.typesTreeView.GetSelectedNodes();
			}

			doc.Decompile(this.CurrentLanguage, sharpTreeNodes, new DecompilationOptions() { TextViewState = state }, addToHistory: recordInHistory);
		}
		
		void SaveCommandExecuted(object sender, ExecutedRoutedEventArgs e)
		{
			if(this.CurrentDocument == null)
				return;
			
			if (this.SelectedNodes.Count() == 1) {
				if (this.SelectedNodes.Single().Save(this.CurrentDocument.DecompilerTextView))
					return;
			}
			this.CurrentDocument.DecompilerTextView.SaveToDisk(this.CurrentLanguage,
										                         this.SelectedNodes,
										                         new DecompilationOptions() { FullDecompilation = true });
		}
		
		public void RefreshDecompiledView()
		{
			DecompileSelectedNodes(null, newWindow: false, recordInHistory: false);
		}
		
		public Language CurrentLanguage {
			get {
				return sessionSettings.FilterSettings.Language;
			}
		}
		
		public IEnumerable<ILSpyTreeNode> SelectedNodes {
			get {
				return treeView.GetTopLevelSelection().OfType<ILSpyTreeNode>();
			}
		}
		#endregion
		
		#region Back/Forward navigation
		void BackCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
		{
			e.Handled = true;
			var ds = this.CurrentDocument;
			e.CanExecute = ds != null && ds.History.CanNavigateBack;
		}
		
		void BackCommandExecuted(object sender, ExecutedRoutedEventArgs e)
		{
			if (NavigateHistory(forward: false))
				e.Handled = true;
		}
		
		void ForwardCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
		{
			e.Handled = true;
			var ds = this.CurrentDocument;
			e.CanExecute = ds != null && ds.History.CanNavigateForward;
		}
		
		void ForwardCommandExecuted(object sender, ExecutedRoutedEventArgs e)
		{
			if (NavigateHistory(forward: true))
				e.Handled = true;
		}

		bool NavigateHistory(bool forward)
		{
			var session = this.CurrentDocument;
			if(session == null)
				return false;
			var history = session.History;
			
			var historyEntry = new NavigationHistoryEntry(typesTreeView.GetSelectedNodesPaths(), session.DecompilerTextView.GetState());
			
			while(forward ? history.CanNavigateForward : history.CanNavigateBack) {
				var newState = forward ? history.GoForward(historyEntry) : history.GoBack(historyEntry);
				
				var treeNodes = newState.SelectedNodes.Select(nd => typesTreeView.FindNodeByPath(nd, false)).Where(tn => tn != null).ToList();
				if(treeNodes.Count != 0) {
					DecompileSelectedNodes(treeNodes, newWindow: false, recordInHistory: false);
					return true;
				}
				historyEntry = null;	// Do not store it again in the history.
			}
			
			// Remove state saved by the first navigation attempt if none succeeded.
			if(historyEntry == null)
				if(forward)
					history.GoBack(null);
				else
					history.GoForward(null);
			return false;
		}
		
		#endregion
		
		#region Analyzer
		public void AddToAnalyzer(AnalyzerTreeNode node)
		{
			if (analyzerTree.Root == null)
				analyzerTree.Root = new AnalyzerTreeNode { Language = sessionSettings.FilterSettings.Language };
			
			node.IsExpanded = true;
			analyzerTree.Root.Children.Add(node);
			analyzerTree.SelectedItem = node;
			analyzerTree.FocusNode(node);
			analyzerContent.Show();
		}
		#endregion
		
		protected override void OnStateChanged(EventArgs e)
		{
			base.OnStateChanged(e);
			// store window state in settings only if it's not minimized
			if (this.WindowState != System.Windows.WindowState.Minimized)
				sessionSettings.WindowState = this.WindowState;
		}
		
		protected override void OnClosing(CancelEventArgs e)
		{
			base.OnClosing(e);
			sessionSettings.ActiveAssemblyList = assemblyList.ListName;
			sessionSettings.ActiveTreeViewPath = typesTreeView.GetPathForNode(treeView.SelectedItem as SharpTreeNode);
			sessionSettings.WindowBounds = this.RestoreBounds;

			// Prepare saving sessions and layout (they both need correct document names)
			PrepareSavingDocuments();
			sessionSettings.SaveSettings(SaveDocuments("{uri://sharpdevelop.net/ilspy}OpenDocuments"));
			var xDoc= new XDocument();
			using(var writer = xDoc.CreateWriter())
				dockManager.SaveLayout(writer);
			sessionSettings.DockManagerSettings = xDoc.Root;
			sessionSettings.ActiveTreeViewPath = null;
			sessionSettings.Save();
		}
		
		void ShowAnalyzer_Click(object sender, RoutedEventArgs e)
		{
			analyzerContent.Show();
		}
		
		#region DecompilerDocuments
		DecompilerDocument CurrentDocument
		{
			get
			{
				var doc = this.dockManager.ActiveDocument;
				if(doc != null)
					return doc.Tag as DecompilerDocument;
				else
					return null;
			}
		}
		
		DecompilerDocument CurrentOrDefaultDocument
		{
			get
			{
				var doc = CurrentDocument;
				if(doc == null) {
					if(ListDocuments().Any(d => d == lastActiveDocument))
						doc = lastActiveDocument;
				}
				return doc;
			}
		}
		
		DecompilerDocument OpenNewDocument(bool hidden = false)
		{
			var d = new DecompilerDocument(this.typesTreeView);
			if(hidden)
				this.documentPane.Items.Add(d.Document);
			else {
				d.Document.Show(dockManager);
				d.Document.Activate();
			}
			d.Document.IsActiveDocumentChanged += delegate { SheduleNextAction(() => ActivateDocument(d)); };
			return d;
		}
		
		IEnumerable<DecompilerDocument> ListDocuments()
		{
			return this.dockManager.Documents
				.Select(d => d.Tag as DecompilerDocument)
				.Where(ds => ds != null);
		}
		
		IEnumerable<DecompilerDocument> ListVisibleDocuments()
		{
			return ListDocuments()
				.Where(d => Selector.GetIsSelected(d.Document));
		}
		
		void LoadDocuments(XElement el)
		{
			foreach (var sessionEl in el.Elements("Document")) {
				var ss = OpenNewDocument(hidden: true);
				ss.Load(sessionEl);
			}
		}

		void PrepareSavingDocuments()
		{
			int n = 0;
			foreach (var doc in ListDocuments()) {
				if(doc.SelectedNodes.Count > 0)
					doc.Document.Name = "Doc" + n++;
				else
					doc.Document.Name = null;
			}
		}

		XElement SaveDocuments(XName name)
		{
			var sessions = ListDocuments()
				.Where(doc => doc.Document.Name != null)
				.Select(ss => ss.Save("Document"));
			
			return new XElement(name, sessions);
		}
		
		class NavigationHistoryEntry
		{
			public DecompilerTextViewState TextViewState;
			public List<string[]> SelectedNodes;
			
			public NavigationHistoryEntry(List<string[]> selectedNodes, DecompilerTextViewState textViewState)
			{
				this.SelectedNodes = selectedNodes;
				TextViewState = textViewState;
			}
			
			public void Load(XElement el)
			{
				
			}
			
			public XElement Save(XName name)
			{
				return null;
			}
		}

		class DecompilerDocument
		{
			public readonly NavigationHistory<NavigationHistoryEntry> History =
				new NavigationHistory<NavigationHistoryEntry>();
			public readonly DecompilerTextView DecompilerTextView;
			public readonly DocumentContent Document;
			public readonly ReadOnlyCollection<string[]> SelectedNodes;
			
			private List<string[]> selectedNodes;
			private TypesTreeView typesTreeView;
			private Language language;
			private bool decompiled;
			
			public DecompilerDocument(TypesTreeView typesTreeView)
			{
				if (typesTreeView == null)
					throw new ArgumentNullException("typesTreeView");
				this.typesTreeView = typesTreeView;
				this.selectedNodes = new List<string[]>();
				this.SelectedNodes = new ReadOnlyCollection<string[]>(this.selectedNodes);
				this.DecompilerTextView = App.CompositionContainer.GetExportedValue<DecompilerTextView>();
				this.Document = new DocumentContentWithActivation {
					Title = "Decompiler",
					Content = this.DecompilerTextView,
					Tag = this,
				};
			}
			
			public string Title
			{
				get
				{
					return this.Document.Title;
				}
				set
				{
					this.Document.Title = value;
				}
			}
			
			public void Decompile(Language language, IEnumerable<SharpTreeNode> treeNodes, DecompilationOptions options, bool addToHistory)
			{
				var selectedTreeNodes = treeNodes == null ? new List<ILSpyTreeNode>() : treeNodes.OfType<ILSpyTreeNode>().ToList();

				if(addToHistory && (this.selectedNodes.Count > 0))
					this.History.Record(new NavigationHistoryEntry(this.SelectedNodes.ToList(), this.DecompilerTextView.GetState()));
				this.selectedNodes.Clear();
				this.selectedNodes.AddRange(selectedTreeNodes.Select(nd => typesTreeView.GetPathForNode(nd)));

				if (selectedTreeNodes.Count == 1) {
					if (selectedTreeNodes[0].View(this.DecompilerTextView)) {
						this.Title = selectedTreeNodes[0].Text.ToString();
						return;
					}
				}
				
				if(selectedTreeNodes.Count > 1)
					this.Title = String.Format("{0} (+{1} more)", selectedTreeNodes[0].Text, selectedNodes.Count - 1);
				else if(selectedTreeNodes.Count == 1)
					this.Title = selectedTreeNodes[0].Text.ToString();
				else
					this.Title = "(none)";
				
				this.DecompilerTextView.Decompile(language, selectedTreeNodes, options ?? new DecompilationOptions());
			}
			
			public bool Activate(Language language)
			{
				var treeNodes = this.selectedNodes
					.Select(nd => typesTreeView.FindNodeByPath(nd, returnBestMatch: false))
					.Where(nd => nd != null)
					.ToList();
				if(treeNodes.Count == 0 && this.selectedNodes.Count > 0)
					return false;
				this.typesTreeView.SilentlySelectNodes(treeNodes);
				if(!this.decompiled || language != this.language)
					Decompile(language, treeNodes, null, addToHistory: false);
				this.language = language;
				this.decompiled = true;
				return true;
			}
			
			public void ResetDecompilationState()
			{
				this.decompiled = false;
			}
			
			public void Load(XElement el)
			{
				this.Title = (string)el.Attribute("title");
				this.Document.Name = (string)el.Attribute("name");
				foreach (var nodeEl in el.Elements("node")) {
					this.selectedNodes.Add(LoadNode(nodeEl));
				}
			}

			public XElement Save(XName name)
			{
				return new XElement(name,
				                    new XAttribute("name", this.Document.Name),
				                    new XAttribute("title", this.Title),
				                    this.SelectedNodes.Select(n => SaveNode(n, "node")));
			}
			
			XElement SaveNode(string[] path, XName name)
			{
				if(!path.Any(p => p.Contains("/")))
					return new XElement(name, new XAttribute("path", String.Join("/", path)));
				else
					return new XElement(name, path.Select(p => new XElement("p", p)));
			}

			string[] LoadNode(XElement el)
			{
				string path = (string)el.Attribute("path");
				if(path != null)
					return path.Split('/');
				else
					return el.Elements("p").Select(p => (string)p).ToArray();
			}
		}
	
		sealed class TypesTreeView
		{
			private SharpTreeView treeView;
			private bool ignoreSelectionChanges;
			
			public event SelectionChangedEventHandler SelectionChanged;
			
			public TypesTreeView(SharpTreeView treeView)
			{
				if (treeView == null)
					throw new ArgumentNullException("treeView");
				this.treeView = treeView;
				this.treeView.SelectionChanged += (sender, e) => OnSelectionChanged(e);
			}

			void OnSelectionChanged(SelectionChangedEventArgs e)
			{
				if(this.ignoreSelectionChanges)
					return;
				
				var handler = this.SelectionChanged;
				if(handler != null)
					handler(this, e);
			}

			/// <summary>
			/// Sets root node of the types tree without sending notification about selection changes.
			/// </summary>
			/// <param name="rootNode">New root node.</param>
			/// <remarks>The caller is reponsible for updating views.</remarks>
			public void SetRootNodes(SharpTreeNode rootNode)
			{
				ignoreSelectionChanges = true;
				this.treeView.Root = rootNode;
				ignoreSelectionChanges = false;
			}
			
			/// <summary>
			/// Retrieves a node using the .ToString() representations of its ancestors.
			/// </summary>
			public SharpTreeNode FindNodeByPath(string[] path, bool returnBestMatch)
			{
				if (path == null)
					return null;
				SharpTreeNode node = treeView.Root;
				SharpTreeNode bestMatch = node;
				foreach (var element in path) {
					if (node == null)
						break;
					bestMatch = node;
					node.EnsureLazyChildren();
					node = node.Children.FirstOrDefault(c => c.ToString() == element);
				}
				if (returnBestMatch)
					return node ?? bestMatch;
				else
					return node;
			}
			
			/// <summary>
			/// Gets the .ToString() representation of the node's ancestors.
			/// </summary>
			public string[] GetPathForNode(SharpTreeNode node)
			{
				if (node == null)
					return null;
				List<string> path = new List<string>();
				while (node.Parent != null) {
					path.Add(node.ToString());
					node = node.Parent;
				}
				path.Reverse();
				return path.ToArray();
			}
			
			public IEnumerable<SharpTreeNode> GetSelectedNodes()
			{
				return treeView
					.SelectedItems
					.OfType<SharpTreeNode>();
			}
			
			public List<string[]> GetSelectedNodesPaths()
			{
				return treeView
					.SelectedItems
					.OfType<SharpTreeNode>()
					.Select(nd => GetPathForNode(nd))
					.ToList();
			}
			
			/// <summary>
			/// Selects nodes without triggering decompilation.
			/// </summary>
			/// <param name="nodes">The nodes to select</param>
			public void SilentlySelectNodes(List<SharpTreeNode> nodes)
			{
				this.ignoreSelectionChanges = true;
				treeView.SelectedItems.Clear();
				foreach (var node in nodes)
				{
					treeView.SelectedItems.Add(node);
				}
				if (nodes.Any()) {
					treeView.FocusNode(nodes.First());
					treeView.ScrollIntoView(nodes.First());
				}
				this.ignoreSelectionChanges = false;
			}
			
		}
		
		
		/// <summary>
		///  The document content class that will activate itself after any mouse click.
		/// </summary>
		/// <remarks>
		/// This is a workaround for a problem with clicking on a reference in inactive document.
		/// </remarks>
		class DocumentContentWithActivation : DocumentContent
		{
			protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
			{
				Activate();
			}
		}
		
		#endregion
		
		DecompilerTextView IDocumentsManager.OpenTextView(string title)
		{
			Debug.WriteLine(String.Format("Creating TextView document with title '{0}'", title));
			var textView = App.CompositionContainer.GetExportedValue<DecompilerTextView>();
			var d = new DocumentContent
			{
				Content = textView,
				Title = title,
			};
			d.Show(this.dockManager);
			d.Activate();
			return textView;
		}
	}
	
	public interface IDocumentsManager
	{
		DecompilerTextView OpenTextView(string title);
	}
}