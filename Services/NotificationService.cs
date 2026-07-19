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
                var toastTemplate = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
                var textNodes = toastTemplate.GetElementsByTagName("text");
                textNodes[0].AppendChild(toastTemplate.CreateTextNode(title));
                textNodes[1].AppendChild(toastTemplate.CreateTextNode(message));

                var toast = new ToastNotification(toastTemplate);
                ToastNotificationManager.CreateToastNotifier().Show(toast);  // 修复：ShowToast → Show
            }
            catch
            {
                // 通知失败时静默忽略
            }
        }
    }
}