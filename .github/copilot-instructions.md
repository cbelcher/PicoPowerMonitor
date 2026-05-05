# Copilot Instructions

## Project Guidelines
- This project uses WinUI 3 (not WPF). Avoid WPF-only Window properties (SizeToContent, WindowStartupLocation, MinWidth, MinHeight) in XAML; do sizing/positioning via WinUI code-behind (measure/Width/Height) or the AppWindow API.