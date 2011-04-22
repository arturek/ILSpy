// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under MIT X11 license (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using ICSharpCode.TreeView;
using System.Diagnostics;

namespace ICSharpCode.ILSpy
{
	/// <summary>
	/// Stores the navigation history.
	/// </summary>
	internal sealed class NavigationHistory<T>
		where T : class, IEquatable<T>
	{
		private const double NavigationSecondsBeforeNewEntry = 0.5;

		private DateTime lastNavigationTime = DateTime.MinValue;
		T current;
		List<T> back = new List<T>();
		List<T> forward = new List<T>();
		
		public bool CanNavigateBack {
			get { return back.Count > 0; }
		}
		
		public bool CanNavigateForward {
			get { return forward.Count > 0; }
		}
		
		public T GoBack()
		{
			forward.Add(current);
			current = back[back.Count - 1];
			back.RemoveAt(back.Count - 1);
			return current;
		}
		
		public T GoForward()
		{
			back.Add(current);
			current = forward[forward.Count - 1];
			forward.RemoveAt(forward.Count - 1);
			return current;
		}

		public void RemoveAll(Predicate<T> predicate)
		{
			back.RemoveAll(predicate);
			forward.RemoveAll(predicate);
		}
		
		public void Clear()
		{
			back.Clear();
			forward.Clear();
		}
		
		public void Record(T node, bool replace = false, bool clearForward = true)
		{
			var navigationTime = DateTime.Now;
			var period = navigationTime - lastNavigationTime;

			if (period.TotalSeconds < NavigationSecondsBeforeNewEntry || replace) {
				current = node;
			} else {
				if (current != null)
					back.Add(current);

				// We only store a record once, and ensure it is on the top of the stack, so we just remove the old record
				back.Remove(node);
				current = node;
			}

			if (clearForward)
				forward.Clear();

			lastNavigationTime = navigationTime;
		}
		
		public XElement Save(XName name, Func<T, XName, XElement> elementSaver)
		{
			if (name == null)
				throw new ArgumentNullException("name");
			if (elementSaver == null)
				throw new ArgumentNullException("elementSaver");
			
			if(back.Count == 0 && forward.Count == 0)
				return null;
			
			return new XElement(name,
								current != null ? elementSaver(current, "current") : null,
			                    new XElement("back",
			                                 back.Select(t => elementSaver(t, "entry"))),
			                    new XElement("forward",
			                                 forward.Select(t => elementSaver(t, "entry"))));
		}
		
		public void Load(XElement el, Func<XElement, T> elementLoader)
		{
			if (elementLoader == null)
				throw new ArgumentNullException("elementLoader");

			if(el == null)
				return;

			var currentEl = el.Element("current");
			if (currentEl != null)
				current = elementLoader(currentEl);

			back.AddRange(el.Element("back").Elements("entry").Select(ent => elementLoader(ent)));
			forward.AddRange(el.Element("forward").Elements("entry").Select(ent => elementLoader(ent)));
		}
	}
}
