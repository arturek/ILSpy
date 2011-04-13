using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ICSharpCode.TreeView;

namespace ICSharpCode.ILSpy.TreeNodes
{
	[ExportContextMenuEntry(Header = "Open in New Tab", Icon = "images/Open.png")]
	sealed class OpenInNewTabContextMenuEntry : IContextMenuEntry
	{
		public bool IsVisible(SharpTreeNode[] selectedNodes)
		{
			return selectedNodes.All(nd => nd is IMemberTreeNode || nd is AssemblyTreeNode);
		}

		public bool IsEnabled(SharpTreeNode[] selectedNodes)
		{
			return true;
		}

		public void Execute(SharpTreeNode[] selectedNodes)
		{
			MainWindow.Instance.DecompileSelectedNodes(selectedNodes.ToList(), newWindow: true, recordInHistory: false);
		}
	}
}
