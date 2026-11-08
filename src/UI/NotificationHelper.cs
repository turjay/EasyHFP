using System;
using System.Security;
using System.Windows.Forms;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace EasyHFP.UI
{
    public static class NotificationHelper
    {
        public static void ShowToast(string title, string message, NotifyIcon? fallbackIcon = null)
        {
            try
            {
                string toastXml = string.Format(
                    "<toast><visual><binding template=\"ToastGeneric\"><text>{0}</text><text>{1}</text></binding></visual></toast>",
                    SecurityElement.Escape(title),
                    SecurityElement.Escape(message)
                );

                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(toastXml);

                var toast = new ToastNotification(xmlDoc);
                var notifier = ToastNotificationManager.CreateToastNotifier("EasyHFP Bluetooth Manager");
                notifier.Show(toast);
                return;
            }
            catch
            {
                // Fall back to NotifyIcon balloon
                if (fallbackIcon != null)
                {
                    try
                    {
                        fallbackIcon.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);
                    }
                    catch { }
                }
            }
        }
    }
}
