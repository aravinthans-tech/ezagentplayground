"""
InsightFace Face Matching Service
FastAPI service that provides face detection and verification using InsightFace.
"""
import os
import base64
import numpy as np
from typing import Optional, Dict, Any
from fastapi import FastAPI, File, UploadFile, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from PIL import Image
import io
import logging

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

app = FastAPI(title="InsightFace Face Matching Service", version="1.0.0")

# Enable CORS
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Global variables for InsightFace models
face_analyzer = None
face_detector = None
models_ready = False  # Flag to track when models are fully loaded and ready
models_loading = False  # Flag to track if models are currently downloading

def initialize_insightface():
    """Initialize InsightFace models."""
    global face_analyzer, face_detector
    try:
        import insightface
        from insightface.app import FaceAnalysis
        import os
        from pathlib import Path
        import glob
        
        logger.info("Initializing InsightFace...")
        
        # First, check for pre-downloaded models in local directory (for Docker)
        script_dir = Path(__file__).parent
        local_models_dir = script_dir / "models" / "buffalo_l"
        local_onnx_files = list(local_models_dir.glob("*.onnx")) if local_models_dir.exists() else []
        
        if local_onnx_files:
            logger.info(f"✅ Found {len(local_onnx_files)} pre-downloaded model file(s) in local directory")
            logger.info(f"   Using models from: {local_models_dir}")
            logger.info("   (Models are pre-baked in Docker image - no download needed)")
            
            # Use local models directory as root
            try:
                # Try different initialization methods for different InsightFace versions
                model_root = str(local_models_dir.parent)
                
                # Method 1: Try with name, root, and providers (for newer versions that support it)
                try:
                    face_analyzer = FaceAnalysis(name='buffalo_l', root=model_root, providers=['CPUExecutionProvider'])
                    face_analyzer.prepare(ctx_id=-1, det_size=(640, 640))
                    logger.info("✅ InsightFace initialized with pre-downloaded models (method 1)")
                    return True
                except (TypeError, AssertionError):
                    # Method 2: Try with just name and root (for versions that don't support providers parameter)
                    try:
                        face_analyzer = FaceAnalysis(name='buffalo_l', root=model_root)
                        face_analyzer.prepare(ctx_id=-1, det_size=(640, 640))
                        logger.info("✅ InsightFace initialized with pre-downloaded models (method 2)")
                        return True
                    except (TypeError, AssertionError):
                        # Method 3: Set environment variable and use default initialization
                        # Some versions use INSIGHTFACE_ROOT environment variable
                        os.environ['INSIGHTFACE_ROOT'] = model_root
                        face_analyzer = FaceAnalysis(providers=['CPUExecutionProvider'])
                        face_analyzer.prepare(ctx_id=-1, det_size=(640, 640))
                        logger.info("✅ InsightFace initialized with pre-downloaded models (method 3)")
                        return True
            except Exception as e:
                logger.warning(f"Failed to use local models: {e}")
                logger.info("Falling back to default model location...")
                # Clear any environment variables we might have set
                if 'INSIGHTFACE_ROOT' in os.environ:
                    del os.environ['INSIGHTFACE_ROOT']
        
        # Fallback: Check default location or auto-download
        logger.info("Checking default model location or downloading models...")
        logger.info("Note: Model download may take 2-5 minutes on first run...")
        
        # Initialize face analysis with default model
        # Handle different InsightFace API versions
        try:
            # Try newer API first (with providers parameter - version 0.7+)
            logger.info("Attempting to initialize InsightFace 0.7.3+ (with auto-download)...")
            face_analyzer = FaceAnalysis(providers=['CPUExecutionProvider'])
            logger.info("FaceAnalysis created, preparing models (this may download models)...")
            face_analyzer.prepare(ctx_id=-1, det_size=(640, 640))
            logger.info("✅ InsightFace models prepared successfully")
        except (TypeError, AssertionError):
            # Fall back to older API (0.2.1 and earlier)
            # Version 0.2.1 requires 'name' parameter and models must be downloaded first
            logger.info("Using InsightFace 0.2.1 API - downloading models...")
            try:
                import insightface.model_zoo.model_zoo as model_zoo
                import os
                
                # Expand the root path
                model_root = os.path.expanduser('~/.insightface/models')
                os.makedirs(model_root, exist_ok=True)
                
                # For InsightFace 0.2.1, models must be downloaded manually
                # Check if models exist first
                import glob
                buffalo_path = os.path.join(model_root, 'buffalo_l')
                onnx_files = glob.glob(os.path.join(buffalo_path, '*.onnx')) if os.path.exists(buffalo_path) else []
                
                if not onnx_files:
                    logger.error("=" * 60)
                    logger.error("MODELS NOT FOUND!")
                    logger.error("=" * 60)
                    logger.error("InsightFace 0.2.1 requires models to be downloaded manually.")
                    logger.error(f"Expected location: {buffalo_path}")
                    logger.error("")
                    logger.error("SOLUTION: Upgrade InsightFace to fix this issue:")
                    logger.error("  pip uninstall insightface")
                    logger.error("  pip install insightface==0.7.3")
                    logger.error("")
                    logger.error("OR download models manually from:")
                    logger.error("  https://github.com/deepinsight/insightface")
                    logger.error("  Extract to: ~/.insightface/models/buffalo_l/")
                    logger.error("=" * 60)
                    raise Exception("Models not found. Please upgrade InsightFace or download models manually.")
                
                if onnx_files:
                    logger.info(f"Found {len(onnx_files)} model file(s), initializing FaceAnalysis...")
                else:
                    logger.info("Initializing FaceAnalysis (will attempt to load/download models)...")
                
                # Initialize FaceAnalysis - it will try to load models
                # For 0.2.1, this will fail if models aren't present
                # For newer versions, it will auto-download
                try:
                    face_analyzer = FaceAnalysis(name='buffalo_l', root=model_root)
                    face_analyzer.prepare(ctx_id=-1, det_size=(640, 640))
                except AssertionError as ae:
                    # Try default root
                    logger.info("Trying with default root path...")
                    try:
                        face_analyzer = FaceAnalysis(name='buffalo_l')
                        face_analyzer.prepare(ctx_id=-1, det_size=(640, 640))
                    except AssertionError:
                        # Models not found - allow service to start anyway for testing
                        logger.warning("=" * 60)
                        logger.warning("INSIGHTFACE MODELS NOT FOUND")
                        logger.warning("=" * 60)
                        logger.warning("Face detection will NOT work, but service will start for testing.")
                        logger.warning("")
                        logger.warning("To enable face detection:")
                        logger.warning("1. Download models from: https://github.com/deepinsight/insightface")
                        logger.warning(f"2. Extract to: {buffalo_path}")
                        logger.warning("")
                        logger.warning("OR use Docker/Render deployment (Linux works better)")
                        logger.warning("=" * 60)
                        # Don't raise - allow service to start for integration testing
                        face_analyzer = None
                        return False
            except Exception as e2:
                logger.error(f"Failed to initialize InsightFace 0.2.1: {e2}", exc_info=True)
                logger.warning("Face detection will be unavailable. Service will continue running.")
                logger.warning("Note: InsightFace 0.2.1 may have compatibility issues. Consider upgrading to 0.7.3+ for better support.")
                # Don't raise - allow service to start without face detection
                return False
        
        logger.info("✅ InsightFace initialized successfully - models are ready")
        return True
    except ImportError as e:
        logger.error(f"InsightFace not installed. Please install: pip install insightface. Error: {e}")
        return False
    except Exception as e:
        logger.error(f"Failed to initialize InsightFace: {e}", exc_info=True)
        return False

@app.on_event("startup")
async def startup_event():
    """Initialize models on startup - wait for models to be ready before accepting requests."""
    import asyncio
    
    global models_loading, models_ready
    
    models_loading = True
    logger.info("Starting InsightFace model initialization...")
    logger.info("This may take 1-2 minutes if models need to be loaded from disk...")
    
    try:
        # Run initialization in thread pool to avoid blocking event loop
        # But we wait for it to complete before the service is ready
        loop = asyncio.get_event_loop()
        success = await loop.run_in_executor(None, initialize_insightface)
        
        if success:
            models_ready = True
            models_loading = False
            logger.info("✅ InsightFace models loaded and ready for face detection")
            logger.info("Service is now ready to accept face detection requests")
        else:
            models_loading = False
            logger.warning("⚠️ InsightFace initialization failed. Face detection will be unavailable.")
            logger.warning("Service will start, but face detection requests will return errors.")
    except Exception as e:
        models_loading = False
        logger.error(f"Error during model initialization: {e}", exc_info=True)
        logger.warning("Service will start, but face detection may be unavailable.")
    
    # Note: Service startup is complete, but models_ready flag controls request handling

@app.get("/")
async def root():
    """Health check endpoint."""
    return {
        "status": "running",
        "service": "InsightFace Face Matching",
        "insightface_loaded": face_analyzer is not None,
        "models_ready": models_ready,
        "models_loading": models_loading
    }

@app.get("/health")
async def health():
    """Health check endpoint - responds immediately for Render's 30s timeout."""
    # Always return healthy immediately (even if models are loading)
    # This ensures Render doesn't kill the service during model download
    return {
        "status": "healthy",
        "insightface_loaded": face_analyzer is not None,
        "models_ready": models_ready,
        "models_loading": models_loading,
        "face_detection_available": models_ready and face_analyzer is not None
    }

def image_to_numpy(image_bytes: bytes) -> np.ndarray:
    """Convert image bytes to numpy array."""
    try:
        image = Image.open(io.BytesIO(image_bytes))
        # Convert to RGB if necessary
        if image.mode != 'RGB':
            image = image.convert('RGB')
        return np.array(image)
    except Exception as e:
        logger.error(f"Error converting image to numpy: {e}")
        raise HTTPException(status_code=400, detail=f"Invalid image format: {str(e)}")

@app.post("/detect")
async def detect_face(file: UploadFile = File(...)):
    """
    Detect face in an image and return face embedding.
    
    Returns:
        - faceId: Base64-encoded face embedding
        - confidence: Detection confidence score
        - bbox: Bounding box coordinates [x, y, width, height]
    """
    # Check if models are ready
    if not models_ready or face_analyzer is None:
        if models_loading:
            return {
                "faceId": None,
                "confidence": 0.0,
                "bbox": None,
                "message": "⏳ Models are currently downloading. Please wait 30-60 seconds and try again. This only happens on first startup."
            }
        else:
            return {
                "faceId": None,
                "confidence": 0.0,
                "bbox": None,
                "message": "❌ InsightFace models not loaded. Face detection is unavailable."
            }
    
    try:
        # Read image bytes
        image_bytes = await file.read()
        if len(image_bytes) == 0:
            raise HTTPException(status_code=400, detail="Empty image file")
        
        # Convert to numpy array
        img = image_to_numpy(image_bytes)
        
        # Detect faces
        faces = face_analyzer.get(img)
        
        if len(faces) == 0:
            return {
                "faceId": None,
                "confidence": 0.0,
                "bbox": None,
                "message": "No face detected in image"
            }
        
        # Get the largest face (most likely the main subject)
        largest_face = max(faces, key=lambda f: (f.bbox[2] - f.bbox[0]) * (f.bbox[3] - f.bbox[1]))
        
        # Extract face embedding (normalized)
        embedding = largest_face.normed_embedding
        
        # Encode embedding as base64 for transmission
        face_id = base64.b64encode(embedding.tobytes()).decode('utf-8')
        
        # Get detection confidence (det_score)
        confidence = float(largest_face.det_score) if hasattr(largest_face, 'det_score') else 0.95
        
        # Get bounding box
        bbox = {
            "x": int(largest_face.bbox[0]),
            "y": int(largest_face.bbox[1]),
            "width": int(largest_face.bbox[2] - largest_face.bbox[0]),
            "height": int(largest_face.bbox[3] - largest_face.bbox[1])
        }
        
        logger.info(f"Face detected with confidence: {confidence:.2f}")
        
        return {
            "faceId": face_id,
            "confidence": confidence,
            "bbox": bbox,
            "message": "Face detected successfully"
        }
        
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error detecting face: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Face detection failed: {str(e)}")

@app.post("/verify")
async def verify_faces(data: Dict[str, Any]):
    """
    Verify if two faces belong to the same person.
    
    Request body:
        - faceId1: Base64-encoded face embedding from first image
        - faceId2: Base64-encoded face embedding from second image
    
    Returns:
        - isIdentical: Boolean indicating if faces match
        - confidence: Similarity score (0.0 to 1.0)
        - matchScore: Integer score from 0-5
    """
    # Check if models are ready
    if not models_ready or face_analyzer is None:
        if models_loading:
            return {
                "isIdentical": False,
                "confidence": 0.0,
                "matchScore": 0,
                "threshold": 0.65,
                "message": "⏳ Models are currently downloading. Please wait 30-60 seconds and try again. This only happens on first startup."
            }
        else:
            return {
                "isIdentical": False,
                "confidence": 0.0,
                "matchScore": 0,
                "threshold": 0.65,
                "message": "❌ InsightFace models not loaded. Face verification is unavailable."
            }
    
    try:
        face_id1 = data.get("faceId1")
        face_id2 = data.get("faceId2")
        
        if not face_id1 or not face_id2:
            raise HTTPException(status_code=400, detail="Both faceId1 and faceId2 are required")
        
        # Decode embeddings
        try:
            embedding1_bytes = base64.b64decode(face_id1)
            embedding2_bytes = base64.b64decode(face_id2)
            
            embedding1 = np.frombuffer(embedding1_bytes, dtype=np.float32)
            embedding2 = np.frombuffer(embedding2_bytes, dtype=np.float32)
        except Exception as e:
            raise HTTPException(status_code=400, detail=f"Invalid face ID format: {str(e)}")
        
        # Calculate cosine similarity
        # Since embeddings are normalized, cosine similarity = dot product
        similarity = np.dot(embedding1, embedding2)
        
        # Clamp similarity to [0, 1] range
        similarity = max(0.0, min(1.0, similarity))
        
        # Threshold for matching (typically 0.6-0.7 for InsightFace)
        # Using 0.65 as a reasonable threshold
        threshold = 0.65
        is_identical = similarity >= threshold
        
        # Convert similarity to match score (0-5 scale)
        # Map similarity [0.5, 1.0] to score [0, 5]
        if similarity < 0.5:
            match_score = 0
        else:
            match_score = int(round((similarity - 0.5) / 0.1))  # Maps 0.5->0, 0.6->1, ..., 1.0->5
            match_score = min(5, max(0, match_score))
        
        logger.info(f"Face verification: similarity={similarity:.3f}, isIdentical={is_identical}, matchScore={match_score}")
        
        return {
            "isIdentical": is_identical,
            "confidence": float(similarity),
            "matchScore": match_score,
            "threshold": threshold
        }
        
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error verifying faces: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Face verification failed: {str(e)}")

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=5001)

