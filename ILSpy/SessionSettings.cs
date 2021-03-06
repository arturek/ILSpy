﻿// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
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
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Xml.Linq;

namespace ICSharpCode.ILSpy
{
	/// <summary>
	/// Per-session setting:
	/// Loaded at startup; saved at exit.
	/// </summary>
	sealed class SessionSettings : INotifyPropertyChanged
	{
		public SessionSettings(ILSpySettings spySettings)
		{
			XElement doc = spySettings["SessionSettings"];
			
			XElement filterSettings = doc.Element("FilterSettings");
			if (filterSettings == null) filterSettings = new XElement("FilterSettings");
			
			this.FilterSettings = new FilterSettings(filterSettings);
			
			this.ActiveAssemblyList = (string)doc.Element("ActiveAssemblyList");
			
			XElement activeTreeViewPath = doc.Element("ActiveTreeViewPath");
			if (activeTreeViewPath != null) {
				this.ActiveTreeViewPath = activeTreeViewPath.Elements().Select(e => (string)e).ToArray();
			}
			
			this.WindowState = FromString((string)doc.Element("WindowState"), WindowState.Normal);
			this.WindowBounds = FromString((string)doc.Element("WindowBounds"), new Rect(10, 10, 750, 550));
			
			var layoutElement = doc.Element("Layout");
			if(layoutElement != null)
				this.DockManagerSettings = layoutElement.Elements().First();
			
			var componentsElement = doc.Element("Components") ?? new XElement("Components");
			this.components = componentsElement.Elements().ToList();
		}
		
		public event PropertyChangedEventHandler PropertyChanged;
		
		void OnPropertyChanged(string propertyName)
		{
			if (PropertyChanged != null)
				PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
		}
		
		public FilterSettings FilterSettings { get; private set; }
		
		public string[] ActiveTreeViewPath;
		
		public string ActiveAssemblyList;
		
		public WindowState WindowState = WindowState.Normal;
		public Rect WindowBounds;
		
		public XElement DockManagerSettings;
		
		private List<XElement> components;
		
		public XElement GetSettings(XName name)
		{
			if (name == null)
				throw new ArgumentNullException("name");
			return this.components.FirstOrDefault(el => el.Name == name);
		}
		
		public void SaveSettings(XElement settings)
		{
			if(settings == null)
				throw new ArgumentNullException("settings");
			
			for (int i = 0; i < this.components.Count; i++) {
				if(components[i].Name == settings.Name) {
					components[i] = settings;
					return;
				}
			}
			components.Add(settings);
		}
		
		public void Save()
		{
			XElement doc = new XElement("SessionSettings");
			doc.Add(this.FilterSettings.SaveAsXml());
			if (this.ActiveAssemblyList != null) {
				doc.Add(new XElement("ActiveAssemblyList", this.ActiveAssemblyList));
			}
			if (this.ActiveTreeViewPath != null) {
				doc.Add(new XElement("ActiveTreeViewPath", ActiveTreeViewPath.Select(p => new XElement("Node", p))));
			}
			doc.Add(new XElement("WindowState", ToString(this.WindowState)));
			doc.Add(new XElement("WindowBounds", ToString(this.WindowBounds)));
			if(this.DockManagerSettings != null){
				doc.Add(new XElement("Layout", this.DockManagerSettings));
			}
			
			doc.Add(new XElement("Components", this.components));
			
			ILSpySettings.SaveSettings(doc);
		}
		
		static T FromString<T>(string s, T defaultValue)
		{
			if (s == null)
				return defaultValue;
			try {
				TypeConverter c = TypeDescriptor.GetConverter(typeof(T));
				return (T)c.ConvertFromInvariantString(s);
			} catch (FormatException) {
				return defaultValue;
			}
		}
		
		static string ToString<T>(T obj)
		{
			TypeConverter c = TypeDescriptor.GetConverter(typeof(T));
			return c.ConvertToInvariantString(obj);
		}
	}
}
