﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fiddler;
using Debug = System.Diagnostics.Debug;

namespace d_f_32.KanColleCacher
{
	enum Direction
	{
		Discharge_Response,		//无关请求，或需要下载文件 -> 忽略请求
		Return_LocalFile,		//存在无需验证的本地文件 -> 返回本地文件
		Verify_LocalFile,		//验证文件有效性 -> 向服务器发送验证请求
	}

    class FiddlerRules
    {
        static CacheCore cache;
		
		static public void Initialize ()
		{
			cache = new CacheCore();
			
			_AppendToFiddler();
		}
		
        static void _AppendToFiddler()
        {
            FiddlerApplication.BeforeRequest += _BeforeRequest;
            FiddlerApplication.BeforeResponse += _BeforeResponse;
            FiddlerApplication.AfterSessionComplete += _AfterComplete;
        }

		static bool _Filter(Session oSession)
		{
			return oSession.PathAndQuery.StartsWith("/kcs/");
		}

        #region     Fiddler向服务器发送客户端请求前执行的动作
        //This event fires when a client request is received by Fiddler
        static void _BeforeRequest(Session oSession)
        {
            if (!Settings.Current.CacheEnabled) return;
			if (!_Filter(oSession)) return;

            string result;
			var direction = cache.GotNewRequest(oSession.fullUrl, out result);
			
			if (direction == Direction.Return_LocalFile)
			{
				//返回本地文件
				oSession.utilCreateResponseAndBypassServer();
				oSession.ResponseBody = File.ReadAllBytes(result);
				_CreateResponseHeader(oSession, result);

				//Debug.WriteLine("CACHR> 【返回本地】" + result);
			}
			else if (direction == Direction.Verify_LocalFile)
			{
				//请求服务器验证文件
				oSession.oRequest.headers["If-Modified-Since"] = result;
				oSession.bBufferResponse = true;

				//Debug.WriteLine("CACHR> 【验证文件】" + oSession.PathAndQuery);
			}
			else 
			{ 
				//下载文件
			}
        }
        #endregion

        #region     Fiddler向客户端返回服务器响应前执行的动作
        //This event fires when a server response is received by Fiddler
        static void _BeforeResponse(Session oSession)
        {
			if (!_Filter(oSession)) return;
			if (oSession.responseCode == 304)
			{
				string filepath = TaskRecord.GetAndRemove(oSession.fullUrl);
				if (!string.IsNullOrEmpty(filepath))
				{
					//服务器返回304，文件没有修改 -> 返回本地文件
					oSession.bBufferResponse = true;
					oSession.ResponseBody = File.ReadAllBytes(filepath);
					oSession.oResponse.headers.HTTPResponseCode = 200;
					oSession.oResponse.headers.HTTPResponseStatus = "200 OK";
					oSession.oResponse.headers["Last-Modified"] = oSession.oRequest.headers["If-Modified-Since"];
					oSession.oResponse.headers["Accept-Ranges"] = "bytes";
					oSession.oResponse.headers.Remove("If-Modified-Since");
					oSession.oRequest.headers.Remove("If-Modified-Since");
					if (filepath.EndsWith(".swf"))
						oSession.oResponse.headers["Content-Type"] = "application/x-shockwave-flash";

					//Debug.WriteLine(oSession.oResponse.headers.ToString());
					//Debug.WriteLine("");
					//Debug.WriteLine("CACHR> 【捕获 304】" + oSession.PathAndQuery);
					//Debug.WriteLine("		" + filepath);
				}
			}
        }
        #endregion

        #region     会话完成时执行的动作
        static void _AfterComplete(Session oSession)
        {
            if (!Settings.Current.CacheEnabled) return;
			if (!_Filter(oSession)) return;

			if (oSession.responseCode == 200)
			{
				string filepath = TaskRecord.GetAndRemove(oSession.fullUrl);

				if (!string.IsNullOrEmpty(filepath))
				{
					if (File.Exists(filepath))
						File.Delete(filepath);

					//保存下载文件并记录Modified-Time
					try
					{
						oSession.SaveResponseBody(filepath);
						cache.RecordNewModifiedTime(oSession.fullUrl,
							oSession.oResponse.headers["Last-Modified"]);

						//Debug.WriteLine("CACHR> 【下载文件】" + oSession.PathAndQuery);
					}
					catch (Exception ex)
					{
						Log.Exception(oSession, ex, "会话结束时，保存返回文件时发生异常");
					}
				}
			}
        }
        #endregion

		static void _CreateResponseHeader(Session oSession, string filename)
		{
			oSession.oResponse.headers["Server"] = "Apache";
			oSession.oResponse.headers["Cache-Control"] = "max-age=18000, public";
			oSession.oResponse.headers["Date"] = GMTHelper.ToGMTString(DateTime.Now);
			oSession.oResponse.headers["Connection"] = "close";
			oSession.oResponse.headers["Accept-Ranges"] = "bytes";
			filename.ToLower();
			if (filename.EndsWith(".swf"))
				oSession.oResponse.headers["Content-Type"] = "application/x-shockwave-flash";
			else if (filename.EndsWith(".mp3"))
				oSession.oResponse.headers["Content-Type"] = "audio/mpeg";
			else if (filename.EndsWith(".png"))
				oSession.oResponse.headers["Content-Type"] = "image/png";
			
		}







        #region 保留的代码
        //
        //_BeforeResponse = {
        //        oSession.utilDecodeResponse();
        //        oSession.ResponseBody = File.ReadAllBytes(filename);
        //        oSession.oResponse.headers.HTTPResponseCode = 200;
        //        oSession.oResponse.headers.HTTPResponseStatus = "200 OK";
        //    }
        //
        //_BeforeRequest = {
        //        oSession.bBufferResponse = true;
        //    }

        #endregion

    }
}
