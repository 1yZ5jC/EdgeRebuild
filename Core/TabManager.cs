using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.ObjectModel;

namespace EdgeRebuild.Core
{

    public class TabManager
    {
        private IBrowserTab _currentTab;

        public ObservableCollection<IBrowserTab> Tabs { get; } = new ObservableCollection<IBrowserTab>();

        public IBrowserTab CurrentTab
        {
            get => _currentTab;
            set
            {
                if (_currentTab == value) return;
                _currentTab = value;
                CurrentTabChanged?.Invoke(value);
            }
        }

        public event Action<IBrowserTab> CurrentTabChanged;

        public IBrowserTab AddTab(IBrowserTab tab)
        {
            if (tab == null) throw new ArgumentNullException(nameof(tab));
            Tabs.Add(tab);
            CurrentTab = tab;
            return tab;
        }

        public void CloseTab(IBrowserTab tab)
        {
            if (tab == null) return;
            int index = Tabs.IndexOf(tab);
            if (index < 0) return;

            if (CurrentTab == tab)
            {
                // 切换到相邻标签
                IBrowserTab newTab = null;
                if (Tabs.Count > 1)
                {
                    if (index > 0) newTab = Tabs[index - 1];
                    else newTab = Tabs[1]; // 移除第一个后，原来的第二个变为新第一个
                }
                CurrentTab = newTab;
            }

            Tabs.Remove(tab);
            tab.Dispose();
        }
    }
}