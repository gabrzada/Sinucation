/*******************************************************************************
 * Copyright 2024 Intel Corporation
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files(the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions :
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 ******************************************************************************/
#pragma once

#include <stdio.h>
#include <strsafe.h>
#include "Unity/IUnityLog.h"

// log
#define XESS_UNITY_LOG(MSG_) XeSSLog::Log(kUnityLogTypeLog, MSG_, __FILE__, __LINE__)
#define XESS_UNITY_LOG_WARNING(MSG_) XeSSLog::Log(kUnityLogTypeWarning, MSG_, __FILE__, __LINE__)
#define XESS_UNITY_LOG_ERROR(MSG_) XeSSLog::Log(kUnityLogTypeError, MSG_, __FILE__, __LINE__)

//log format
#define XESS_UNITY_LOG_FORMAT(FMT_, ...) XeSSLog::LogFormat(kUnityLogTypeLog, __FILE__, __LINE__, FMT_, __VA_ARGS__)
#define XESS_UNITY_LOG_FORMAT_WARNING(FMT_, ...) XeSSLog::LogFormat(kUnityLogTypeWarning, __FILE__, __LINE__, FMT_, __VA_ARGS__)
#define XESS_UNITY_LOG_FORMAT_ERROR(FMT_, ...) XeSSLog::LogFormat(kUnityLogTypeError, __FILE__, __LINE__, FMT_, __VA_ARGS__)

class XeSSLog
{
	static IUnityLog* s_UnityLogPtr;
	static const char s_prefix[32];
	static const int s_reverse;

public:
	static void SetUnityLog(IUnityLog* logPtr);

	static void Log(UnityLogType type, const char* message, const char* fileName, const int fileLine);
	static void LogFormat(UnityLogType type, const char* fileName, const int fileLine, const char* format, ...);
};