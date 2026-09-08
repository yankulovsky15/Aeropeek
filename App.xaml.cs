using System.Windows;

namespace Aeropeek;

public partial class App : Application
{
    // Une erreur qui se répète — une liaison fautive sur chaque ligne d'une liste,
    // par exemple — produisait autant de boîtes de dialogue que d'occurrences,
    // jusqu'au débordement de pile. On n'en montre qu'une par message.
    readonly HashSet<string> _reported = new();
    bool _reporting;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;

            var key = args.Exception.GetType().Name + "|" + args.Exception.Message;
            if (_reporting || !_reported.Add(key)) return;

            try
            {
                _reporting = true;
                MessageBox.Show(
                    "Aeropeek hit an unexpected error:\n\n" + args.Exception.Message +
                    "\n\nNothing was changed by this action. The journal is intact and " +
                    "“Undo everything” still works.\n\n" +
                    "This message will not repeat for the same error.",
                    "Aeropeek", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { _reporting = false; }
        };
        base.OnStartup(e);
    }
}
