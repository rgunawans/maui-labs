// Blazor JS initializer for Microsoft.Maui.DevFlow.Blazor
// Automatically injects chobitsu.js script tag before Blazor starts

export function beforeStart(options, extensions) {
    // Check if chobitsu is already loaded (e.g. manual script tag)
    if (typeof chobitsu !== 'undefined') {
        return;
    }

    // Check if script tag already exists
    if (document.querySelector('script[src*="chobitsu"]')) {
        return;
    }

    // Inject chobitsu.js script tag (fire-and-forget — C# side polls for availability).
    // Use the _content/ path for Razor class library static web assets.
    const script = document.createElement('script');
    script.src = '_content/Microsoft.Maui.DevFlow.Blazor/chobitsu.js';
    script.async = true;
    document.head.appendChild(script);
}
