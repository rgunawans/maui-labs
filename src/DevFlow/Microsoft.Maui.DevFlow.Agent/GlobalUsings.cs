// Namespace aliases to prevent resolution conflicts under the Microsoft.* namespace hierarchy.
// Without these, e.g. Android.Views resolves to Microsoft.Android.Views.
#if ANDROID
global using Android = global::Android;
global using AndroidX = global::AndroidX;
#endif
#if WINDOWS
global using Windows = global::Windows;
#endif
