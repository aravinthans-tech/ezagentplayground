"""
Script to download InsightFace models locally for Docker image inclusion.

This script downloads the InsightFace buffalo_l models to face_service/models/buffalo_l/
so they can be baked into the Docker image and avoid download delays during deployment.

Usage:
    python download_models.py

Note:
    - Requires InsightFace 0.7.3+ for automatic download
    - If you have InsightFace 0.2.1, upgrade first: pip install insightface==0.7.3
    - Docker will use InsightFace 0.7.3+ from requirements.txt, which auto-downloads models
    - Alternative: Build Docker image once, then copy models from container to local directory
"""
import os
import sys
import logging
from pathlib import Path

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

def download_models():
    """Download InsightFace models to local directory."""
    try:
        import insightface
        from insightface.app import FaceAnalysis
    except ImportError:
        logger.error("InsightFace not installed. Please install it first:")
        logger.error("  pip install insightface onnxruntime")
        sys.exit(1)
    
    # Determine the local models directory
    script_dir = Path(__file__).parent
    models_dir = script_dir / "models" / "buffalo_l"
    models_dir.mkdir(parents=True, exist_ok=True)
    
    logger.info("=" * 60)
    logger.info("Downloading InsightFace Models")
    logger.info("=" * 60)
    logger.info(f"Target directory: {models_dir}")
    logger.info("")
    logger.info("This may take 2-5 minutes depending on your internet connection...")
    logger.info("")
    
    try:
        # Initialize FaceAnalysis - this will trigger model download
        # Handle different InsightFace API versions
        logger.info("Initializing InsightFace (this will download models)...")
        
        face_analyzer = None
        try:
            # Try newer API first (with providers parameter - version 0.7+)
            logger.info("Attempting InsightFace 0.7.3+ API...")
            face_analyzer = FaceAnalysis(providers=['CPUExecutionProvider'])
            face_analyzer.prepare(ctx_id=-1, det_size=(640, 640))
            logger.info("✅ InsightFace 0.7.3+ initialized successfully")
        except (TypeError, AssertionError) as e:
            # Fall back to older API (0.2.1 and earlier)
            logger.info(f"Newer API not available ({e}), trying older API (0.2.1)...")
            try:
                # For older versions, we need to specify name and let it download
                # First check if models already exist
                default_model_path = Path.home() / ".insightface" / "models" / "buffalo_l"
                if default_model_path.exists():
                    logger.info(f"Found existing models at: {default_model_path}")
                    face_analyzer = FaceAnalysis(name='buffalo_l')
                    face_analyzer.prepare(ctx_id=-1, det_size=(640, 640))
                    logger.info("✅ InsightFace 0.2.1 initialized successfully with existing models")
                else:
                    logger.warning("=" * 60)
                    logger.warning("InsightFace 0.2.1 requires manual model download")
                    logger.warning("=" * 60)
                    logger.warning("For InsightFace 0.2.1, models must be downloaded manually.")
                    logger.warning("")
                    logger.warning("SOLUTION 1 (Recommended): Upgrade to InsightFace 0.7.3+")
                    logger.warning("  pip uninstall insightface")
                    logger.warning("  pip install insightface==0.7.3")
                    logger.warning("")
                    logger.warning("SOLUTION 2: Download models manually")
                    logger.warning("  1. Download from: https://github.com/deepinsight/insightface/releases")
                    logger.warning("  2. Extract buffalo_l.zip to: ~/.insightface/models/buffalo_l/")
                    logger.warning("  3. Run this script again")
                    logger.warning("")
                    logger.warning("Note: Docker/Render will use InsightFace 0.7.3+ which auto-downloads models.")
                    logger.warning("=" * 60)
                    return False
            except Exception as e2:
                logger.error(f"Failed to initialize InsightFace 0.2.1: {e2}", exc_info=True)
                logger.error("")
                logger.error("Troubleshooting:")
                logger.error("1. Upgrade InsightFace: pip install insightface==0.7.3")
                logger.error("2. Or download models manually (see instructions above)")
                return False
        
        if face_analyzer is None:
            logger.error("Failed to initialize InsightFace")
            return False
        
        # Get the default model path where InsightFace downloaded models
        default_model_path = Path.home() / ".insightface" / "models" / "buffalo_l"
        
        if default_model_path.exists():
            logger.info(f"Found models at default location: {default_model_path}")
            logger.info("Copying models to local directory...")
            
            # Copy all model files to our local directory
            import shutil
            if models_dir.exists():
                shutil.rmtree(models_dir)
            shutil.copytree(default_model_path, models_dir)
            
            logger.info(f"✅ Models copied to: {models_dir}")
            
            # Verify files were copied
            onnx_files = list(models_dir.glob("*.onnx"))
            if onnx_files:
                logger.info(f"✅ Found {len(onnx_files)} model file(s):")
                for f in onnx_files:
                    size_mb = f.stat().st_size / (1024 * 1024)
                    logger.info(f"   - {f.name} ({size_mb:.1f} MB)")
            else:
                logger.warning("⚠️  No .onnx files found. Models may not have downloaded correctly.")
                return False
            
            logger.info("")
            logger.info("=" * 60)
            logger.info("✅ Model download complete!")
            logger.info("=" * 60)
            logger.info(f"Models are now available at: {models_dir}")
            logger.info("These models will be included in the Docker image.")
            logger.info("")
            return True
        else:
            logger.warning(f"⚠️  Models not found at default location: {default_model_path}")
            logger.warning("InsightFace may have downloaded to a different location.")
            logger.warning("Please check ~/.insightface/models/ manually.")
            return False
            
    except Exception as e:
        logger.error(f"❌ Error downloading models: {e}", exc_info=True)
        logger.error("")
        logger.error("Troubleshooting:")
        logger.error("1. Ensure you have internet connection")
        logger.error("2. Check that InsightFace is installed: pip install insightface")
        logger.error("3. Try running with admin/sudo if permission issues occur")
        return False

if __name__ == "__main__":
    success = download_models()
    sys.exit(0 if success else 1)

