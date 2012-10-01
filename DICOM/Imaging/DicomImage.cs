﻿using System;
using System.Collections.Generic;
#if !SILVERLIGHT
using System.Drawing;
using System.Drawing.Imaging;
#endif
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text;
using Dicom;
using Dicom.Imaging.Codec;
using Dicom.Imaging.LUT;
using Dicom.Imaging.Render;

namespace Dicom.Imaging {
	/// <summary>
	/// DICOM Image calss with capability of
	/// </summary>
	public class DicomImage {
		#region Private Members
		private const int OverlayColor = unchecked((int)0xffff00ff);

		private int _currentFrame;
		private IPixelData _pixelData;
		private IPipeline _pipeline;

		private double _scale;
		private GrayscaleRenderOptions _renderOptions;

		private DicomOverlayData[] _overlays;
		#endregion

		/// <summary>Creates DICOM image object from dataset</summary>
		/// <param name="dataset">Source dataset</param>
		/// <param name="frame">Zero indexed frame number</param>
		public DicomImage(DicomDataset dataset, int frame = 0) {
			_scale = 1.0;
			Load(dataset, frame);
		}

#if !SILVERLIGHT
		/// <summary>Creates DICOM image object from file</summary>
		/// <param name="fileName">Source file</param>
		/// <param name="frame">Zero indexed frame number</param>
		public DicomImage(string fileName, int frame = 0) {
			_scale = 1.0;
			var file = DicomFile.Open(fileName);
			Load(file.Dataset, frame);
		}
#endif

		/// <summary>Source DICOM dataset</summary>
		public DicomDataset Dataset {
			get;
			private set;
		}

		/// <summary>DICOM pixel data</summary>
		public DicomPixelData PixelData {
			get;
			private set;
		}

		/// <summary>Width of image in pixels</summary>
		public int Width {
			get { return PixelData.Width; }
		}

		/// <summary>Height of image in pixels</summary>
		public int Height {
			get { return PixelData.Height; }
		}

		/// <summary>Scaling factor of the rendered image</summary>
		public double Scale {
			get { return _scale; }
			set {
				_scale = value;
				_pixelData = null;
			}
		}

		/// <summary>Number of frames contained in image data.</summary>
		public int NumberOfFrames {
			get { return PixelData.NumberOfFrames; }
		}

		/// <summary>Window width of rendered gray scale image </summary>
		public double WindowWidth {
			get {
				return _renderOptions != null ? _renderOptions.WindowWidth : 0;
			}
			set {
				if (_renderOptions != null) {
					_renderOptions.WindowWidth = value;
				}
			}
		}

		/// <summary>Window center of rendered gray scale image </summary>
		public double WindowCenter {
			get {
				return _renderOptions != null ? _renderOptions.WindowCenter : 0;
			}
			set {

				if (_renderOptions != null) {
					_renderOptions.WindowCenter = value;
				}
			}
		}

#if !SILVERLIGHT
		/// <summary>Renders DICOM image to System.Drawing.Image</summary>
		/// <param name="frame">Zero indexed frame number</param>
		/// <returns>Rendered image</returns>
		public Image RenderImage(int frame = 0) {
			if (frame != _currentFrame || _pixelData == null)
				Load(Dataset, frame);

			CreatePipeline();

			ImageGraphic graphic = new ImageGraphic(_pixelData);

			foreach (var overlay in _overlays) {
				OverlayGraphic og = new OverlayGraphic(PixelDataFactory.Create(overlay), overlay.OriginX, overlay.OriginY, OverlayColor);
				graphic.AddOverlay(og);
			}

			return graphic.RenderImage(_pipeline.LUT);
		}
#endif
		/// <summary>
		/// Renders DICOM image to <typeparamref name="System.Windows.Media.ImageSource"/> 
		/// </summary>
		/// <param name="frame">Zero indexed frame nu,ber</param>
		/// <returns>Rendered image</returns>
		public ImageSource RenderImageSource(int frame = 0) {
			if (frame != _currentFrame || _pixelData == null)
				Load(Dataset, frame);

			CreatePipeline();

			ImageGraphic graphic = new ImageGraphic(_pixelData);

			foreach (var overlay in _overlays) {
				OverlayGraphic og = new OverlayGraphic(PixelDataFactory.Create(overlay), overlay.OriginX, overlay.OriginY, OverlayColor);
				graphic.AddOverlay(og);
			}

			return graphic.RenderImageSource(_pipeline.LUT);
		}


		/// <summary>
		/// Loads the <para>dataset</para> pixeldata for specified frame and set the internal dataset
		/// 
		/// </summary>
		/// <param name="dataset">dataset to load pixeldata from</param>
		/// <param name="frame">The frame number to create pixeldata for</param>
		private void Load(DicomDataset dataset, int frame) {
			Dataset = dataset;
			if (Dataset.InternalTransferSyntax.IsEncapsulated) {
				DicomCodecParams cparams = null;
				if (Dataset.InternalTransferSyntax == DicomTransferSyntax.JPEGProcess1) {
					cparams = new DicomJpegParams {
						ConvertColorspaceToRGB = true
					};
				}

				//Is this introduce performance problem when dealing with multi-frame image?
				Dataset = Dataset.ChangeTransferSyntax(DicomTransferSyntax.ExplicitVRLittleEndian, cparams);
			}

			if (PixelData == null)
				PixelData = DicomPixelData.Create(Dataset);

			_pixelData = PixelDataFactory.Create(PixelData, frame);
			_pixelData.Rescale(_scale);

			_overlays = DicomOverlayData.FromDataset(Dataset).Where(x => x.Type == DicomOverlayType.Graphics && x.Data != null).ToArray();

			_currentFrame = frame;
		}

		/// <summary>
		/// Create image rendering pipeline according to the Dataset <see cref="PhotometricInterpretation"/>.
		/// </summary>
		private void CreatePipeline() {
			if (_pipeline != null)
				return;

			var pi = Dataset.Get<PhotometricInterpretation>(DicomTag.PhotometricInterpretation);

			if (pi == null) {
				// generally ACR-NEMA
				var samples = Dataset.Get<ushort>(DicomTag.SamplesPerPixel, 0, 0);
				if (samples == 0 || samples == 1) {
					if (Dataset.Contains(DicomTag.RedPaletteColorLookupTableData))
						pi = PhotometricInterpretation.PaletteColor;
					else
						pi = PhotometricInterpretation.Monochrome2;
				} else {
					// assume, probably incorrectly, that the image is RGB
					pi = PhotometricInterpretation.Rgb;
				}
			}


			if (pi == PhotometricInterpretation.Monochrome1 || pi == PhotometricInterpretation.Monochrome2) {
				//Monochrom1 or Monochrome2 for grayscale image
				if (_renderOptions == null)
					_renderOptions = GrayscaleRenderOptions.FromDataset(Dataset);
				_pipeline = new GenericGrayscalePipeline(_renderOptions);
			} else if (pi == PhotometricInterpretation.Rgb) {
				//RGB for color image
				_pipeline = new RgbColorPipeline();
			} else if (pi == PhotometricInterpretation.PaletteColor) {
				//PALETTE COLOR for Palette image
				_pipeline = new PaletteColorPipeline(PixelData);
			} else {
				throw new DicomImagingException("Unsupported pipeline photometric interpretation: {0}", pi.Value);
			}
		}
	}
}
