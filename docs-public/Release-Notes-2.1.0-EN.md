# BookStitch 2.1.0 – Release Notes

BookStitch 2.1 adds direct browser cover drops and a new cover-processing pipeline. Existing audiobook projects remain compatible.

### Cover import and preparation
- Drag images directly from supported browsers onto the cover area in the main window or metadata editor, when image data or an accessible image URL is available.
- Covers with a shortest edge of at least 200 pixels are accepted. Automatic square output uses 1000, 2000 or 3000 pixels, with adaptive JPEG quality.
- Choose Automatic, White or Black for transparent cover backgrounds in global metadata settings. The choice applies to newly imported covers.
- Bundled WebP decoding works without an installed Windows WebP extension. Cover processing also handles image orientation and color profiles; ImageSharp is no longer required.
