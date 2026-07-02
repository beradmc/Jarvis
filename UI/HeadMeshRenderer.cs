using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using SharpGLTF.Schema2;
using JarvisCSharp.Core;

namespace JarvisCSharp.UI
{
    public class HeadMeshRenderer
    {
        private struct Vertex
        {
            public float X, Y, Z;
            public float NX, NY, NZ;
        }

        private Vertex[] _vertices = Array.Empty<Vertex>();
        private bool _isLoaded = false;

        public HeadMeshRenderer()
        {
            LoadGlb();
        }

        private void LoadGlb()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "luwai_HD_1780529596324.glb");
                if (!File.Exists(path))
                {
                    // Fallback to project root if running from IDE without copy
                    path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Assets", "luwai_HD_1780529596324.glb");
                }

                if (!File.Exists(path))
                {
                    Logger.Warning("GLB file not found. Hologram will not render.");
                    return;
                }

                var model = ModelRoot.Load(path);
                var rawVertices = new List<Vertex>();

                foreach (var mesh in model.LogicalMeshes)
                {
                    foreach (var primitive in mesh.Primitives)
                    {
                        var positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
                        var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();

                        if (positions != null && normals != null)
                        {
                            for (int i = 0; i < positions.Count; i++)
                            {
                                var p = positions[i];
                                var n = normals[i];
                                rawVertices.Add(new Vertex { X = p.X, Y = p.Y, Z = p.Z, NX = n.X, NY = n.Y, NZ = n.Z });
                            }
                        }
                    }
                }

                // Deduplicate with precision
                var uniqueDict = new Dictionary<string, Vertex>();
                foreach (var v in rawVertices)
                {
                    string key = $"{Math.Round(v.X, 4)}_{Math.Round(v.Y, 4)}_{Math.Round(v.Z, 4)}";
                    if (!uniqueDict.ContainsKey(key))
                    {
                        uniqueDict[key] = v;
                    }
                }

                var uniqueList = uniqueDict.Values.ToList();
                int step = Math.Max(1, uniqueList.Count / 25000);
                
                var sampled = new List<Vertex>();
                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;
                float minZ = float.MaxValue, maxZ = float.MinValue;

                for (int i = 0; i < uniqueList.Count; i += step)
                {
                    var v = uniqueList[i];
                    sampled.Add(v);
                    if (v.X < minX) minX = v.X;
                    if (v.X > maxX) maxX = v.X;
                    if (v.Y < minY) minY = v.Y;
                    if (v.Y > maxY) maxY = v.Y;
                    if (v.Z < minZ) minZ = v.Z;
                    if (v.Z > maxZ) maxZ = v.Z;
                }

                float cx = (minX + maxX) / 2;
                float cy = (minY + maxY) / 2;
                float cz = (minZ + maxZ) / 2;

                float maxDim = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
                float scale = 2.0f / maxDim;

                _vertices = new Vertex[sampled.Count];
                for (int i = 0; i < sampled.Count; i++)
                {
                    var v = sampled[i];
                    _vertices[i] = new Vertex
                    {
                        X = (v.X - cx) * scale,
                        Y = (v.Y - cy) * scale,
                        Z = (v.Z - cz) * scale,
                        NX = v.NX,
                        NY = v.NY,
                        NZ = v.NZ
                    };
                }

                _isLoaded = true;
                Logger.Information($"HeadMesh loaded with {_vertices.Length} points.");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to load HeadMesh GLB");
            }
        }

        public unsafe void Render(WriteableBitmap bmp, double rotX, double rotY, double scale, double cx, double cy, double activity = 1.0, byte baseR = 0, byte baseG = 160, byte baseB = 255)
        {
            if (!_isLoaded || _vertices.Length == 0) return;

            int w = bmp.PixelWidth;
            int h = bmp.PixelHeight;

            bmp.Lock();
            try
            {
                byte* pBackBuffer = (byte*)bmp.BackBuffer;
                int stride = bmp.BackBufferStride;

                // Clear background to #0D1117 (dark blueish gray) or transparent (0)
                // We'll just clear to fully transparent or dark
                for (int y = 0; y < h; y++)
                {
                    int* pRow = (int*)(pBackBuffer + y * stride);
                    for (int x = 0; x < w; x++)
                    {
                        pRow[x] = 0; // Transparent BG
                    }
                }

                float cosRx = (float)Math.Cos(rotX);
                float sinRx = (float)Math.Sin(rotX);
                float cosRy = (float)Math.Cos(rotY);
                float sinRy = (float)Math.Sin(rotY);
                float cam = 5.0f;

                // Fast arrays
                var verts = _vertices;
                int len = verts.Length;

                for (int i = 0; i < len; i++)
                {
                    var v = verts[i];
                    
                    // Vertex Rotation
                    float rx = v.X * cosRy + v.Z * sinRy;
                    float rz = -v.X * sinRy + v.Z * cosRy;
                    float ry = v.Y * cosRx - rz * sinRx;
                    float rz2 = v.Y * sinRx + rz * cosRx;

                    if (rz2 > 0.4f) continue; // Backface culling
                    
                    float p = cam / (cam + rz2);
                    float sx = (float)cx + rx * (float)scale * p;
                    float sy = (float)cy - ry * (float)scale * p;

                    // Normal Rotation
                    float nrx = v.NX * cosRy + v.NZ * sinRy;
                    float nrz = -v.NX * sinRy + v.NZ * cosRy;
                    float nry = v.NY * cosRx - nrz * sinRx;
                    float nrz2 = v.NY * sinRx + nrz * cosRx;

                    // Lighting (Diffuse + Fresnel)
                    float dot = nrx * 0.6f + nry * (-0.4f) + nrz2 * (-0.7f);
                    float diffuse = Math.Max(0.0f, dot);
                    diffuse *= diffuse;

                    float fresnel = Math.Max(0.0f, 1.0f - Math.Abs(nrz2));
                    fresnel = (float)Math.Pow(fresnel, 2.5);

                    float br = (diffuse * 1.5f) + (fresnel * 1.5f) + 0.1f;
                    br *= (float)activity;

                    if (br < 0.05f) continue;

                    int px = (int)sx;
                    int py = (int)sy;

                    if (px >= 0 && px < w - 1 && py >= 0 && py < h - 1)
                    {
                        byte cr = (byte)Math.Min(255, baseR * br);
                        byte cg = (byte)Math.Min(255, baseG * br);
                        byte cb = (byte)Math.Min(255, baseB * br);
                        
                        // BGR32 format
                        int color = (255 << 24) | (cr << 16) | (cg << 8) | cb;
                        
                        int* pPix = (int*)(pBackBuffer + py * stride + px * 4);
                        
                        // Simple Z-ish / Brightness additive blending
                        if (br > 0.3f)
                        {
                            *pPix = color;
                            *(pPix + 1) = color; // px+1
                            int* pPixBelow = (int*)(pBackBuffer + (py + 1) * stride + px * 4);
                            *pPixBelow = color; // py+1
                            *(pPixBelow + 1) = color; // px+1, py+1
                        }
                        else
                        {
                            *pPix = color;
                        }
                    }
                }
                
                bmp.AddDirtyRect(new System.Windows.Int32Rect(0, 0, w, h));
            }
            finally
            {
                bmp.Unlock();
            }
        }
    }
}
