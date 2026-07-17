using System;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace EdgeRebuild.Services
{
    public static class NotificationService
    {
        public static void ShowToast(string title, string message)
        {
            try
            {
                string xml = $@"
                <toast>
                    <visual>
                        <binding template='ToastGeneric'>
                            <text>{title}</text>
                            <text>{message}</text>
                        </binding>
                    </visual>
                </toast>";

                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xml);

                var toast = new ToastNotification(doc);
                ToastNotificationManager.CreateToastNotifier().Show(toast);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"通知发送失败: {ex.Message}");
            }
        }
    }
}