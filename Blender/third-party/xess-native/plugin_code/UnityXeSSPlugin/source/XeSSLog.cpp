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
#include "XeSSLog.h"

#include <cstdarg>
#include <stdlib.h>
#include <string.h>

#define PREFIX_LEN 32
#define MSG_MAX 2048
IUnityLog* XeSSLog::s_UnityLogPtr = NULL;
const char XeSSLog::s_prefix[PREFIX_LEN] = { "XeSS Native Plugin" };
const int XeSSLog::s_reverse = 10;

void XeSSLog::SetUnityLog(IUnityLog* logPtr)
{
	s_UnityLogPtr = logPtr;
}

void XeSSLog::Log(UnityLogType type, const char* message, const char* fileName, const int fileLine)
{
	if (!s_UnityLogPtr)
		return;

	size_t size = strnlen_s(message, MSG_MAX) + strnlen_s(s_prefix, PREFIX_LEN) + s_reverse;
	char* szBuff = new char[size];

	if (_snprintf_s(szBuff, size, size - 1, "%s: %s", s_prefix, message) > 0)
	{
		s_UnityLogPtr->Log(type, szBuff, fileName, fileLine);
	}
	delete[] szBuff;
}

void XeSSLog::LogFormat(UnityLogType type, const char* fileName, const int fileLine, const char* format, ...)
{
	if (!s_UnityLogPtr)
		return;

	va_list arg1, arg2;
	va_start(arg1, format);

	va_copy(arg2, arg1);
	size_t size = _vscprintf(format, arg2) + 1;
	va_end(arg2);

	char* szBuff = new char[size];
	int nSize = vsnprintf_s(szBuff, size, size - 1, format, arg1);
	va_end(arg1);

	if (nSize > 0)
	{
		size_t totalSize = strnlen_s(szBuff, size) + strnlen_s(s_prefix, PREFIX_LEN) + s_reverse;
		char* totalBuff = new char[totalSize];

		if (_snprintf_s(totalBuff, totalSize, totalSize - 1, "%s: %s", s_prefix, szBuff) > 0)
		{
			s_UnityLogPtr->Log(type, totalBuff, fileName, fileLine);
		}
		delete[] totalBuff;
	}
	delete[] szBuff;
}