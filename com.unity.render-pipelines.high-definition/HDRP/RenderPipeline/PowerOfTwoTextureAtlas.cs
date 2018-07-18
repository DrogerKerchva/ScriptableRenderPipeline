﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering.HDPipeline;

namespace UnityEngine.Experimental.Rendering
{
    public class PowerOfTwoTextureAtlas : Texture2DAtlas
    {
        public int mipPadding;

        public PowerOfTwoTextureAtlas(int size, int mipPadding, RenderTextureFormat format, bool generateMipMaps = true, FilterMode filterMode = FilterMode.Point)
            : base(size, size, format, generateMipMaps, filterMode, true)
        {
            this.mipPadding = mipPadding;

            // Check if size is a power of two
            if ((size & (size - 1)) != 0)
                Debug.Assert(false, "Power of two atlas was constructed with non power of two size: " + size);
        }

        int GetTexturePadding(int mipCount)
        {
            return (int)Mathf.Pow(2, Mathf.Min(mipCount, mipPadding)) * 2;
        }
        
        void BlitCubemap(CommandBuffer cmd, Vector4 scaleBias, Texture texture)
        {
            int mipCount = GetTextureMipmapCount(texture);
            int pixelPadding = GetTexturePadding(mipCount);
            Vector2 textureSize = GetPowerOfTwoTextureSize(texture);
            
            for (int mipLevel = 0; mipLevel < mipCount; mipLevel++)
            {
                cmd.SetRenderTarget(m_AtlasTexture, mipLevel);
                HDUtils.BlitCubeOctahedral(cmd, texture, textureSize, new Vector4(2, 2, -1, -1), scaleBias, mipLevel, true, pixelPadding);
            }
        }

        void Blit2DTextureRepeat(CommandBuffer cmd, Vector4 scaleBias, Texture texture)
        {
            int mipCount = GetTextureMipmapCount(texture);
            int pixelPadding = GetTexturePadding(mipCount);
            Vector2 textureSize = GetPowerOfTwoTextureSize(texture);

            using (new ProfilingSample(cmd, "Blit cubemap texture"))
            {
                for (int mipLevel = 0; mipLevel < mipCount; mipLevel++)
                {
                    cmd.SetRenderTarget(m_AtlasTexture, mipLevel);
                    HDUtils.BlitPaddedQuad(cmd, texture, textureSize, new Vector4(1, 1, 0, 0), scaleBias, mipLevel, true, pixelPadding);
                }
            }
        }

        bool IsCubemap(Texture texture)
        {
            CustomRenderTexture crt = texture as CustomRenderTexture;

            return (texture is Cubemap || (crt != null && crt.dimension == TextureDimension.Cube));
        }

        protected override void BlitTexture(CommandBuffer cmd, Vector4 scaleBias, Texture texture)
        {
            // We handle ourself the 2D blit because cookies needs mipPadding for trilinear filtering
            if (Is2D(texture))
                Blit2DTextureRepeat(cmd, scaleBias, texture);

            if (IsCubemap(texture))
                BlitCubemap(cmd, scaleBias, texture);
        }

        void TextureSizeToPowerOfTwo(Texture texture, ref int width, ref int height)
        {
            if (IsCubemap(texture))
            {
                // Octahedron size correction
                width = Mathf.ClosestPowerOfTwo((int)Mathf.Sqrt(width * width * 6));
                height = Mathf.ClosestPowerOfTwo((int)Mathf.Sqrt(height * height * 6));
            }
            else
            {
                // Change the width and height of the texture to be power of two
                width = Mathf.NextPowerOfTwo(width);
                height = Mathf.NextPowerOfTwo(height);
            }
        }

        Vector2 GetPowerOfTwoTextureSize(Texture texture)
        {
            int width = texture.width, height = texture.height;
            
            TextureSizeToPowerOfTwo(texture, ref width, ref height);
            return new Vector2(width, height);
        }

        protected override bool AllocateTexture(CommandBuffer cmd, ref Vector4 scaleBias, Texture texture, int width, int height)
        {
            // This atlas only supports square textures
            if (height != width)
                return false;

            TextureSizeToPowerOfTwo(texture, ref height, ref width);

            return base.AllocateTexture(cmd, ref scaleBias, texture, width, height);
        }
        
        public override bool AddTexture(CommandBuffer cmd, ref Vector4 scaleBias, Texture texture)
        {
            // If the texture is 2D or already chached we have nothing to do in this function
            if (base.AddTexture(cmd, ref scaleBias, texture))
                return true;
            
            // We only accept cubemaps and 2D textures for this atlas
            if (!IsCubemap(texture))
                return false;

            bool b = AllocateTexture(cmd, ref scaleBias, texture, texture.width, texture.height);

            return b;
        }
    }
}